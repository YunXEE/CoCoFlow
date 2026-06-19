using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// 极简服务定位器：按类型注册 / 获取 / 等待。
    /// ⚠️ 非线程安全；只在主线程使用。
    /// ⚠️ 同类型多次 Register 会覆盖前一次（适合用 MonoBehaviour 单例）。
    /// </summary>
    public static class CoCoServices
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, List<WaiterEntry>> Waiters = new Dictionary<Type, List<WaiterEntry>>();

        #region Public API

        /// <summary>
        /// 注册一个服务实现。后注册会覆盖前注册。
        /// </summary>
        public static void Register<T>(T impl) where T : class
        {
            if (impl == null) throw new ArgumentNullException(nameof(impl));
            var t = typeof(T);
            if (Services.ContainsKey(t))
            {
                CoCoLog.Warning($"[CoCoServices] 服务 {t.Name} 已存在，正在被覆盖。");
            }
            Services[t] = impl;

            // 通知所有正在等待该服务的对象
            if (Waiters.Remove(t, out var entries))
            {
                var snapshot = entries.ToArray();
                foreach (var entry in snapshot)
                {
                    if (entry == null || entry.IsDisposed) continue;

                    entry.IsDisposed = true;
                    try { entry.Callback?.Invoke(impl); }
                    catch (Exception ex) { CoCoLog.Error($"[CoCoServices] WaitFor 回调异常: {ex}"); }
                }
            }
        }

        /// <summary>
        /// 注销服务。仅当当前注册者就是 impl 时才会真的移除（防止误注销）。
        /// </summary>
        public static void Unregister<T>(T impl) where T : class
        {
            var t = typeof(T);
            if (Services.TryGetValue(t, out var current) && ReferenceEquals(current, impl))
            {
                Services.Remove(t);
            }
        }

        /// <summary>立即获取服务。未注册则返回 null。</summary>
        public static T Get<T>() where T : class
        {
            return Services.TryGetValue(typeof(T), out var s) ? (T)s : null;
        }

        /// <summary>尝试获取服务。</summary>
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

        /// <summary>
        /// 注册时回调；若已注册则立刻同步回调。
        /// 适合解决 Awake 顺序无法保证或跨模块解耦的初始化场景。
        /// </summary>
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

        /// <summary>
        /// 清空所有已注册的服务和等待者。
        /// 通常用于场景切换、单元测试或 Domain Reload 关闭时的清理。
        /// </summary>
        public static void ClearAll()
        {
            Services.Clear();
            Waiters.Clear();
        }

        #endregion

        #region Internal Logic

        private sealed class WaiterEntry
        {
            public WaiterEntry(Action<object> callback)
            {
                Callback = callback;
            }

            public Action<object> Callback { get; }
            public bool IsDisposed { get; set; }
        }

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

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();
            private EmptyDisposable() { }
            public void Dispose() { }
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        /// <summary>
        /// 兼容 "Enter Play Mode Options - Disable Domain Reload" 选项。
        /// 在进入播放模式时重置静态数据。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => ClearAll();
#endif

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
