using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// Logger Console 订阅 CoCoLogEvent，将收到的日志按级别和模块筛选后显示。
namespace CoCoFlow.Editor.Core
{
    /// <summary>订阅日志事件并绘制 Unity 编辑器中的 Logger Console。</summary>
    public class CoCoLoggerWindow : EditorWindow, ICoCoEventListener<CoCoLogEvent>
    {
        #region Public API

        /// <summary>从 Unity 菜单打开日志窗口或将已有窗口置前。</summary>
        [MenuItem("CoCoFlow/Logger Console %l")]
        public static void ShowWindow()
        {
            var window = GetWindow<CoCoLoggerWindow>("CoCoFlow Logger");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        /// <summary>将日志级别映射为编辑器徽标样式，使同级消息采用一致提示。</summary>
        public static CoCoEditorBadgeKind LevelToBadgeKind(CoCoLogLevel level)
        {
            switch (level)
            {
                case CoCoLogLevel.Warning: return CoCoEditorBadgeKind.Warning;
                case CoCoLogLevel.Error: return CoCoEditorBadgeKind.Error;
                default: return CoCoEditorBadgeKind.Neutral;
            }
        }

        /// <summary>收到总线日志后记录该条目，并更新窗口显示。</summary>
        public void OnEvent(ref CoCoLogEvent eventData)
        {
            _data.Add(eventData);
            MarkDirty();
        }

        #endregion

        #region Internal Logic

        private readonly CoCoLoggerWindowData _data = new CoCoLoggerWindowData();

        private bool _autoScroll = true;

        private ListView _logList;
        private VisualElement _emptyState;
        private Label _countLabel;
        private VisualElement _filterRow;
        private Label _filterHeading;
        private Button _clearButton;
        private ToolbarToggle _autoScrollToggle;
        private int _generation;
        private int _lastFilterModuleCount = -1;

        private bool _pendingRefresh;
        private IVisualElementScheduledItem _scheduledRefresh;

        #region Window Lifecycle

        private void OnEnable()
        {
            _data.PreloadKnownModules();
            CoCoEventBus.Subscribe(this);
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged;
            _scheduledRefresh?.Pause();
            _scheduledRefresh = null;
            _pendingRefresh = false;
            CoCoEventBus.Unsubscribe(this);
        }

        private void CreateGUI()
        {
            _generation++;

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

        #endregion

        #region UI Construction

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
            _lastFilterModuleCount = _data.ModuleCount;
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

                if (ColorUtility.TryParseHtmlString(CoCoLoggerWindowData.GetModuleColor(moduleName), out Color color))
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
                badgeText.text = LevelLabel(logEvent.Level);
            }

            row.Q<Label>("time-label").text = logEvent.Timestamp.ToString("HH:mm:ss");

            var moduleLabel = row.Q<Label>("module-label");
            moduleLabel.text = $"[{logEvent.ModuleName}]";
            if (ColorUtility.TryParseHtmlString(CoCoLoggerWindowData.GetModuleColor(logEvent.ModuleName), out Color color))
            {
                moduleLabel.style.color = color;
            }

            row.Q<Label>("class-label").text = $"{logEvent.ClassName}:";
            row.Q<Label>("message-label").text = logEvent.Message;
        }

        #endregion

        #region Refresh Scheduling and Display

        private void MarkDirty()
        {
            _pendingRefresh = true;
            if (_logList == null || _scheduledRefresh != null)
            {
                return;
            }

            int capturedGeneration = _generation;
            _scheduledRefresh = rootVisualElement.schedule.Execute(() =>
            {
                _scheduledRefresh = null;
                if (capturedGeneration != _generation)
                {
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
                _logList.ScrollToItem(_logList.itemsSource.Count - 1);
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

            bool empty = _data.TotalCount == 0;
            _emptyState.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            _logList.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
        }

        #endregion

        #region Localization

        private void OnLanguageChanged()
        {
            RebuildLocalizedTexts();
            RebuildEmptyState();

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

        private static string LevelLabel(CoCoLogLevel level)
        {
            switch (level)
            {
                case CoCoLogLevel.Warning: return CoCoEditorLocalization.Text("Warning", "警告");
                case CoCoLogLevel.Error: return CoCoEditorLocalization.Text("Error", "错误");
                default: return CoCoEditorLocalization.Text("Log", "日志");
            }
        }

        #endregion

        #endregion
    }
}
