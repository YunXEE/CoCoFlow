using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Editor.Core
{
    /// <summary>按收到的日志保存窗口列表、模块可见性和级别筛选状态。</summary>
    public sealed class CoCoLoggerWindowData
    {
        #region Public API

        public const int MaxLogs = 1000;

        public int TotalCount => _logs.Count;

        public IReadOnlyList<CoCoLogEvent> VisibleEvents
        {
            get
            {
                if (_visibleProjection == null)
                {
                    _visibleProjection = new List<CoCoLogEvent>(_logs.Count);
                    foreach (var logEvent in _logs)
                    {
                        if (IsModuleVisible(logEvent.ModuleName))
                        {
                            _visibleProjection.Add(logEvent);
                        }
                    }
                }

                return _visibleProjection;
            }
        }

        public IEnumerable<string> ModuleNames => _moduleFilters.Keys;

        public int ModuleCount => _moduleFilters.Count;

        /// <summary>预先登记包内可筛选的模块，使窗口在尚未收到日志时也能显示其过滤项。</summary>
        public void PreloadKnownModules()
        {
            foreach (var module in KnownModules)
            {
                if (!_moduleFilters.ContainsKey(module.Name))
                {
                    _moduleFilters[module.Name] = true;
                }
            }
        }

        /// <summary>将收到的日志追加到窗口记录，并登记该日志所用的模块。</summary>
        public void Add(in CoCoLogEvent logEvent)
        {
            if (string.IsNullOrEmpty(logEvent.ModuleName))
            {
                if (!_moduleFilters.ContainsKey("Global"))
                {
                    _moduleFilters["Global"] = true;
                }
            }
            else if (!_moduleFilters.ContainsKey(logEvent.ModuleName))
            {
                _moduleFilters[logEvent.ModuleName] = true;
            }

            _logs.Add(logEvent);
            if (_logs.Count > MaxLogs)
            {
                _logs.RemoveAt(0);
            }

            InvalidateProjection();
        }

        /// <summary>清空窗口当前保存的日志记录。</summary>
        public void Clear()
        {
            _logs.Clear();
            InvalidateProjection();
        }

        /// <summary>查询模块在当前窗口筛选中是否可见，用于决定是否绘制该条日志。</summary>
        public bool IsModuleVisible(string moduleName)
        {
            return !_moduleFilters.TryGetValue(moduleName, out bool visible) || visible;
        }

        /// <summary>将指定模块纳入或排除窗口筛选，供后续刷新使用。</summary>
        public void SetModuleVisible(string moduleName, bool visible)
        {
            if (_moduleFilters.ContainsKey(moduleName))
            {
                _moduleFilters[moduleName] = visible;
                InvalidateProjection();
            }
        }

        #endregion

        #region Internal Logic

        private static readonly (string Name, string Color)[] KnownModules =
        {
            ("Core", "#FFA500"),
            ("Camera", "#00FF00"),
            ("Input", "#00D0FF"),
            ("Map", "#808000"),
            ("Network", "#FF0040"),
            ("Persistence", "#FFC0CB"),
            ("UI", "#0066FF"),
            ("Widgets", "#0066FF"),
            ("Panels", "#0066FF"),
            ("Global", "#CCCCCC"),
        };

        private readonly List<CoCoLogEvent> _logs = new List<CoCoLogEvent>(MaxLogs);
        private readonly Dictionary<string, bool> _moduleFilters =
            new Dictionary<string, bool>();

        private List<CoCoLogEvent> _visibleProjection;

        internal static string GetModuleColor(string moduleName)
        {
            foreach (var module in KnownModules)
            {
                if (module.Name == moduleName)
                {
                    return module.Color;
                }
            }

            return "#FFFFFF";
        }

        private void InvalidateProjection()
        {
            _visibleProjection = null;
        }

        #endregion
    }
}
