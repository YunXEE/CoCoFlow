using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.Core
{
    /// <summary>
    /// Logger Console 的数据层（可测单元，方案 §2.2 invariant #1-#7 的承载者）。
    /// 环形缓冲、模块过滤表与总数口径全部在此；渲染层只做投影。
    /// </summary>
    public sealed class CoCoLoggerWindowData
    {
        /// <summary>性能保护上限：超出移除最旧（invariant #7）。</summary>
        public const int MaxLogs = 1000;

        private static readonly string[] KnownModules =
        {
            "Core", "Animation", "Camera", "Input", "Map", "Network",
            "Persistence", "UI", "Widgets", "Panels", "Global"
        };

        private readonly List<CoCoLogEvent> _logs = new List<CoCoLogEvent>(MaxLogs);
        private readonly Dictionary<string, bool> _moduleFilters =
            new Dictionary<string, bool>();

        private List<CoCoLogEvent> _visibleProjection;

        /// <summary>到达总数口径（invariant #5：不受过滤影响）。</summary>
        public int TotalCount => _logs.Count;

        /// <summary>当前过滤投影（invariant #4：只读视图，不删数据）。缓存失效于任何变更。</summary>
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

        /// <summary>过滤项模块名（注册顺序稳定）。</summary>
        public IEnumerable<string> ModuleNames => _moduleFilters.Keys;

        /// <summary>过滤项模块数（BUG-043：窗口据此检测模块集合变化并重建过滤控件）。</summary>
        public int ModuleCount => _moduleFilters.Count;

        /// <summary>预注册已知模块过滤项（默认启用）。窗口 OnEnable 调一次。</summary>
        public void PreloadKnownModules()
        {
            foreach (var module in KnownModules)
            {
                if (!_moduleFilters.ContainsKey(module))
                {
                    _moduleFilters[module] = true;
                }
            }
        }

        /// <summary>
        /// 追加日志（invariant #1/#2/#7）：未知模块默认启用并注册过滤项；
        /// 保持到达顺序；超出上限移除最旧。
        /// </summary>
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

        /// <summary>清空数据（invariant #6：数据清空 + 计数归零；过滤表保留）。</summary>
        public void Clear()
        {
            _logs.Clear();
            InvalidateProjection();
        }

        public bool IsModuleVisible(string moduleName)
        {
            return !_moduleFilters.TryGetValue(moduleName, out bool visible) || visible;
        }

        public void SetModuleVisible(string moduleName, bool visible)
        {
            if (_moduleFilters.ContainsKey(moduleName))
            {
                _moduleFilters[moduleName] = visible;
                InvalidateProjection();
            }
        }

        private void InvalidateProjection()
        {
            _visibleProjection = null;
        }
    }

    /// <summary>
    /// CoCoFlow Logger Console（方案 v4 试点：IMGUI → UI Toolkit + ccflow 视觉语言）。
    /// 不变契约：类名 / 菜单 CoCoFlow&gt;Logger Console %l / minSize / ICoCoEventListener 订阅退订。
    /// 生命周期契约（方案 §3.2）：事件订阅只在 OnEnable/OnDisable；CreateGUI 零订阅；
    /// 帧级刷新带 _generation guard；OnDisable 取消排队刷新。
    /// </summary>
    public class CoCoLoggerWindow : EditorWindow, ICoCoEventListener<CoCoLogEvent>
    {
        // ==========================================
        // 模块识别色（D15：原样迁移，无变体机制；深色 Pro 主题唯一支持形态）
        // ==========================================
        private static readonly Dictionary<string, string> ModuleColors =
            new Dictionary<string, string>
        {
            { "Core" , "#FFA500" },
            { "Animation" , "#800080" },
            { "Camera", "#00FF00" },
            { "Input", "#00D0FF" },
            { "Map" , "#808000" },
            { "Network", "#FF0040" },
            { "Persistence" , "#FFC0CB" },
            { "UI", "#0066FF" },
            { "Widgets", "#0066FF" },
            { "Panels", "#0066FF" },
            { "Global", "#CCCCCC" }
        };

        private const string UnknownModuleColor = "#FFFFFF";

        private readonly CoCoLoggerWindowData _data = new CoCoLoggerWindowData();

        private bool _autoScroll = true;

        // 渲染状态（CreateGUI 持有；订阅零绑定，重建天然安全）
        private ListView _logList;
        private VisualElement _emptyState;
        private Label _countLabel;
        private VisualElement _filterRow;
        private Label _filterHeading;
        private Button _clearButton;
        private ToolbarToggle _autoScrollToggle;
        private int _generation;
        private int _lastFilterModuleCount = -1;

        // 帧级合并刷新（方案 §3.2）
        private bool _pendingRefresh;
        private IVisualElementScheduledItem _scheduledRefresh;

        [MenuItem("CoCoFlow/Logger Console %l")]
        public static void ShowWindow()
        {
            var window = GetWindow<CoCoLoggerWindow>("CoCoFlow Logger");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        /// <summary>Level → badge 语义映射（invariant #9；不新增 Level 过滤开关）。</summary>
        public static CoCoEditorBadgeKind LevelToBadgeKind(CoCoLogLevel level)
        {
            switch (level)
            {
                case CoCoLogLevel.Warning: return CoCoEditorBadgeKind.Warning;
                case CoCoLogLevel.Error: return CoCoEditorBadgeKind.Error;
                default: return CoCoEditorBadgeKind.Neutral;
            }
        }

        /// <summary>BUG-047：等级词双语（固定文案，D10）。</summary>
        private static string LevelLabel(CoCoLogLevel level)
        {
            switch (level)
            {
                case CoCoLogLevel.Warning: return CoCoEditorLocalization.Text("Warning", "警告");
                case CoCoLogLevel.Error: return CoCoEditorLocalization.Text("Error", "错误");
                default: return CoCoEditorLocalization.Text("Log", "日志");
            }
        }

        private void OnEnable()
        {
            _data.PreloadKnownModules();
            CoCoEventBus.Subscribe(this);
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged; // 唯一订阅点
        }

        private void OnDisable()
        {
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged; // 对称退订
            _scheduledRefresh?.Pause();                                    // 暂停排队刷新（陈旧回调另由 _generation guard 丢弃）
            _scheduledRefresh = null;
            _pendingRefresh = false;
            CoCoEventBus.Unsubscribe(this);
        }

        public void OnEvent(ref CoCoLogEvent eventData)
        {
            _data.Add(eventData);
            MarkDirty();
        }

        private void CreateGUI()
        {
            _generation++; // 旧 pending 回调 guard：重建后 _generation 不符即丢弃

            // BUG-048：重建入口暂停旧调度，防竞态窗口内 pending 被旧回调吞掉
            _scheduledRefresh?.Pause();
            _scheduledRefresh = null;

            CoCoEditorElements.ApplyTheme(rootVisualElement);

            BuildToolbar();
            BuildFilterCard();
            BuildLogList();
            RebuildLocalizedTexts();

            if (_pendingRefresh)
            {
                RefreshNow();
            }
        }

        private void BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("ccflow-toolbar");

            _clearButton = new ToolbarButton(() =>
            {
                _data.Clear();
                RefreshNow();
            })
            { name = "clear-button" };

            _autoScrollToggle = new ToolbarToggle { name = "auto-scroll-toggle" };
            _autoScrollToggle.SetValueWithoutNotify(_autoScroll);
            _autoScrollToggle.RegisterValueChangedCallback(evt => _autoScroll = evt.newValue);

            _countLabel = new Label { name = "count-label" };

            toolbar.Add(_clearButton);
            toolbar.Add(_autoScrollToggle);
            toolbar.Add(new VisualElement { style = { flexGrow = 1f } });
            toolbar.Add(_countLabel);
            rootVisualElement.Add(toolbar);
        }

        private void BuildFilterCard()
        {
            var card = CoCoEditorElements.CreateCard(string.Empty);
            _filterHeading = card.Q<Label>("ccflow-card-title");

            _filterRow = new VisualElement { name = "filter-row" };
            _filterRow.style.flexDirection = FlexDirection.Row;
            _filterRow.style.flexWrap = Wrap.Wrap;
            card.Add(_filterRow);
            rootVisualElement.Add(card);

            RebuildFilterToggles();
            _lastFilterModuleCount = _data.ModuleCount; // BUG-043：基线
        }

        private void RebuildFilterToggles()
        {
            if (_filterRow == null)
            {
                return;
            }

            _filterRow.Clear();
            foreach (var module in _data.ModuleNames)
            {
                string moduleName = module;
                var toggle = new Toggle(moduleName)
                {
                    name = $"filter-{moduleName}",
                    value = _data.IsModuleVisible(moduleName)
                };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _data.SetModuleVisible(moduleName, evt.newValue);
                    RefreshNow();
                });

                if (ColorUtility.TryParseHtmlString(GetModuleColor(moduleName), out Color color))
                {
                    toggle.style.color = color;
                }

                toggle.style.marginRight = 8f;
                _filterRow.Add(toggle);
            }
        }

        private void BuildLogList()
        {
            _logList = new ListView
            {
                name = "log-list",
                makeItem = MakeLogRow,
                bindItem = BindLogRow,
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight
            };
            _logList.AddToClassList("ccflow-log-list");
            _logList.itemsSource = new List<CoCoLogEvent>(_data.VisibleEvents);
            rootVisualElement.Add(_logList);
            _logList.style.flexGrow = 1f;

            RebuildEmptyState();
            UpdateEmptyVisibility();
        }

        /// <summary>
        /// BUG-043：空状态按当前语言重建（CreateGUI 与 OnLanguageChanged 均调用）。
        /// 同步修正实施期批量改名误伤的文案（"No _logs yet" → "No logs yet"，随本单留痕）。
        /// </summary>
        private void RebuildEmptyState()
        {
            if (_emptyState != null && _emptyState.parent != null)
            {
                _emptyState.RemoveFromHierarchy();
            }

            _emptyState = CoCoEditorElements.CreateEmptyState(
                CoCoEditorLocalization.Text("No logs yet", "暂无日志"),
                CoCoEditorLocalization.Text(
                    "CoCoLog events will appear here while the project runs.",
                    "工程运行时产生的 CoCoLog 事件将显示在这里。"),
                CoCoEditorLocalization.Text(
                    "Enter Play Mode or trigger any CoCoLog call to produce events.",
                    "进入 Play 模式或触发任意 CoCoLog 调用以产生事件。"),
                CoCoEditorLocalization.Text(
                    "Use the CoCoFlow/Tests injection menu in a test host.",
                    "在测试宿主中使用 CoCoFlow/Tests 注入菜单。"));
            rootVisualElement.Add(_emptyState);
            UpdateEmptyVisibility();
        }

        private static VisualElement MakeLogRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ccflow-list-row");

            var badge = CoCoEditorElements.CreateBadge(
                string.Empty,
                CoCoEditorBadgeKind.Neutral);
            badge.name = "level-badge";
            var time = new Label { name = "time-label" };
            time.AddToClassList("ccflow-muted");
            var module = new Label { name = "module-label" };
            var className = new Label { name = "class-label" };
            className.style.unityFontStyleAndWeight = FontStyle.Bold;
            var message = new Label { name = "message-label" };
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.flexGrow = 1f;

            row.Add(badge);
            row.Add(time);
            row.Add(module);
            row.Add(className);
            row.Add(message);
            return row;
        }

        private void BindLogRow(VisualElement row, int index)
        {
            var projection = _data.VisibleEvents;
            if (index < 0 || index >= projection.Count)
            {
                return;
            }

            CoCoLogEvent logEvent = projection[index];

            var badge = row.Q<VisualElement>("level-badge");
            CoCoEditorElements.SetBadgeKind(badge, LevelToBadgeKind(logEvent.Level));
            var badgeText = badge.Q<Label>("ccflow-badge-text");
            if (badgeText != null)
            {
                // BUG-047：等级词属固定文案，走双语层（D10/invariant #10）
                badgeText.text = LevelLabel(logEvent.Level);
            }

            row.Q<Label>("time-label").text = logEvent.Timestamp.ToString("HH:mm:ss");

            var moduleLabel = row.Q<Label>("module-label");
            moduleLabel.text = $"[{logEvent.ModuleName}]";
            if (ColorUtility.TryParseHtmlString(GetModuleColor(logEvent.ModuleName), out Color color))
            {
                moduleLabel.style.color = color;
            }

            row.Q<Label>("class-label").text = $"{logEvent.ClassName}:";
            row.Q<Label>("message-label").text = logEvent.Message;
        }

        private void MarkDirty()
        {
            _pendingRefresh = true;
            if (_logList == null || _scheduledRefresh != null)
            {
                return; // ListView 未创建：仅置 dirty，CreateGUI 末尾统一刷新
            }

            int capturedGeneration = _generation;
            _scheduledRefresh = rootVisualElement.schedule.Execute(() =>
            {
                _scheduledRefresh = null;
                if (capturedGeneration != _generation)
                {
                    // BUG-048：旧代回调丢弃前，若仍有 pending 工作 → 以当代重新调度，不吞掉
                    if (_pendingRefresh && _logList != null)
                    {
                        MarkDirty();
                    }

                    return;
                }

                RefreshNow();
            });
        }

        private void RefreshNow()
        {
            _pendingRefresh = false;
            if (_logList == null)
            {
                return;
            }

            // BUG-043：模块集合变化（新模块首现）→ 重建过滤控件，开关即时出现
            int moduleCount = _data.ModuleCount;
            if (moduleCount != _lastFilterModuleCount)
            {
                _lastFilterModuleCount = moduleCount;
                RebuildFilterToggles();
            }

            _logList.itemsSource = new List<CoCoLogEvent>(_data.VisibleEvents);
            _logList.RefreshItems();
            UpdateCountLabel();
            UpdateEmptyVisibility();

            if (_autoScroll && _logList.itemsSource.Count > 0)
            {
                _logList.ScrollToItem(_logList.itemsSource.Count - 1); // invariant #8
            }
        }

        private void UpdateCountLabel()
        {
            if (_countLabel != null)
            {
                _countLabel.text = CoCoEditorLocalization.Text(
                    $"Logs: {_data.TotalCount} / {CoCoLoggerWindowData.MaxLogs}",
                    $"日志: {_data.TotalCount} / {CoCoLoggerWindowData.MaxLogs}");
            }
        }

        private void UpdateEmptyVisibility()
        {
            if (_emptyState == null || _logList == null)
            {
                return;
            }

            bool empty = _data.TotalCount == 0; // invariant #10：空状态以数据为准
            _emptyState.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            _logList.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnLanguageChanged()
        {
            RebuildLocalizedTexts();
            RebuildEmptyState(); // BUG-043：空状态四段文案随语言重建

            // BUG-047：重绑列表，使在屏日志行的等级 badge 文本随语言切换
            _logList?.RefreshItems();
        }

        private void RebuildLocalizedTexts()
        {
            if (rootVisualElement == null)
            {
                return;
            }

            if (_clearButton != null)
            {
                _clearButton.text = CoCoEditorLocalization.Text("Clear", "清空");
            }

            if (_autoScrollToggle != null)
            {
                _autoScrollToggle.text = CoCoEditorLocalization.Text("Auto Scroll", "自动滚动");
            }

            if (_filterHeading != null)
            {
                _filterHeading.text = CoCoEditorLocalization.Text("Module Filters", "模块过滤");
            }

            UpdateCountLabel();
        }

        private static string GetModuleColor(string moduleName)
        {
            return ModuleColors.TryGetValue(moduleName, out string color)
                ? color
                : UnknownModuleColor;
        }
    }
}
