using System;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraphHost
{
    /// <summary>
    /// StateGraph Runtime Debugger（UI Toolkit 重做，D6）。
    /// 语义保持集：committed snapshot 只读投影（刷新失败保留上次并标
    /// RetainedStale）、身份切换重置过滤器、Suspend/Resume/One-Tick（有限正
    /// delta 才可用）、128 条可见上限；调试记录经 CoCoLog（D7）。
    /// 菜单：CoCoFlow/StateGraph Debugger（D8 维护者裁决）。
    /// 生命周期：CreateGUI 零订阅；订阅只在 OnEnable/OnDisable 对称；generation
    /// guard + 帧级合并刷新（P02 试点同款契约）。
    /// </summary>
    internal sealed class CoCoStateGraphDebuggerWindow : EditorWindow
    {
        private const string ModuleUssPath =
            "Packages/com.yunxee.cocoflow/Editor/StateGraphHost/CoCoStateGraphHostEditor.uss";

        private static readonly string[] TraceFilterLabels =
        {
            "All",
            "State ID",
            "Transition ID"
        };

        private readonly CoCoStateGraphHostDebuggerState _state =
            new CoCoStateGraphHostDebuggerState();

        private CoCoStateGraphHost _host;
        private bool _followSelection = true;
        private double _deltaTime = 1d / 60d;
        private int _generation;

        // 渲染状态（CreateGUI 持有；零事件订阅）
        private ObjectField _hostField;
        private ToolbarToggle _followToggle;
        private VisualElement _headerBadge;
        private Label _headerIdentity;
        private Button _refreshButton;
        private Button _suspendButton;
        private Button _resumeButton;
        private DoubleField _deltaField;
        private Button _stepButton;
        private VisualElement _controlsCard;
        private VisualElement _metricsCard;
        private Label _metricTick;
        private Label _metricSeconds;
        private Label _metricSequence;
        private Label _metricLayers;
        private Label _metricClaims;
        private VisualElement _snapshotCard;
        private VisualElement _snapshotFreshnessRow;
        private Label _snapshotFreshnessBadge;
        private ListView _snapshotList;
        private System.Collections.Generic.List<CoCoDebuggerSnapshotRow>
            _snapshotRowsCache =
                new System.Collections.Generic.List<CoCoDebuggerSnapshotRow>();
        private VisualElement _traceCard;
        private VisualElement _filterRow;
        private ToolbarToggle[] _filterToggles;
        private TextField _filterIdField;
        private Label _traceCountLabel;
        private ListView _traceList;
        private System.Collections.Generic.List<CoCoDebuggerTraceRow> _traceRowsCache =
            new System.Collections.Generic.List<CoCoDebuggerTraceRow>();
        private VisualElement _emptyState;
        private VisualElement _diagnosticRow;

        // 帧级合并刷新
        private bool _pendingRefresh;
        private IVisualElementScheduledItem _scheduledRefresh;
        private IVisualElementScheduledItem _periodicPoll;

        [MenuItem("CoCoFlow/StateGraph Debugger")]
        internal static void OpenFromMenu()
        {
            CoCoStateGraphDebuggerWindow window =
                GetWindow<CoCoStateGraphDebuggerWindow>();
            window.titleContent = new GUIContent("StateGraph Debugger");
            window.minSize = new Vector2(420f, 320f);
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(CoCoStateGraphHost host)
        {
            CoCoStateGraphDebuggerWindow window =
                GetWindow<CoCoStateGraphDebuggerWindow>();
            window.titleContent = new GUIContent("StateGraph Debugger");
            window.minSize = new Vector2(420f, 320f);
            window._host = host;
            window._followSelection = false;
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged;
            _scheduledRefresh?.Pause();
            _scheduledRefresh = null;
            _periodicPoll?.Pause();
            _periodicPoll = null;
            _pendingRefresh = false;
        }

        private void CreateGUI()
        {
            _generation++;
            _scheduledRefresh?.Pause();
            _scheduledRefresh = null;

            VisualElement root = rootVisualElement;
            CoCoEditorElements.ApplyTheme(root);
            ApplyModuleTheme(root);

            BuildToolbar();
            BuildHeaderCard();
            BuildControlsCard();
            BuildMetricsCard();
            BuildSnapshotCard();
            BuildTraceCard();
            BuildDiagnosticRow();
            BuildEmptyState();

            RebindHostField();
            RefreshNow();

            // Play 期周期轮询（拉取 + 重投影）；重建前旧项必须暂停，防累积。
            _periodicPoll?.Pause();
            _periodicPoll = root.schedule.Execute(() =>
            {
                if (Application.isPlaying && _host != null)
                {
                    MarkDirty();
                }
            }).Every(300);
        }

        private static void ApplyModuleTheme(VisualElement root)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ModuleUssPath);
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        // ===== 结构构建 =====

        private void BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("ccflow-toolbar");

            _hostField = new ObjectField
            {
                allowSceneObjects = true,
                objectType = typeof(CoCoStateGraphHost),
                name = "host-field"
            };
            _hostField.RegisterValueChangedCallback(evt =>
            {
                _host = evt.newValue as CoCoStateGraphHost;
                ObserveAndRefresh();
            });
            _hostField.style.flexGrow = 1f;
            toolbar.Add(_hostField);

            _followToggle = new ToolbarToggle
            {
                text = CoCoEditorLocalization.Text("Follow Selection", "跟随选择"),
                name = "follow-toggle"
            };
            _followToggle.SetValueWithoutNotify(_followSelection);
            _followToggle.RegisterValueChangedCallback(evt =>
            {
                _followSelection = evt.newValue;
                if (_followSelection)
                {
                    TryFollowSelection();
                    RefreshNow();
                }
            });
            toolbar.Add(_followToggle);
            rootVisualElement.Add(toolbar);
        }

        private void BuildHeaderCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(string.Empty);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;

            _headerBadge = CoCoEditorElements.CreateBadge(
                CoCoEditorLocalization.Text("No Host", "无宿主"),
                CoCoEditorBadgeKind.Neutral);
            _headerBadge.style.marginRight = 8f;
            row.Add(_headerBadge);

            _headerIdentity = new Label(string.Empty);
            _headerIdentity.AddToClassList("ccflow-muted");
            _headerIdentity.style.whiteSpace = WhiteSpace.Normal;
            row.Add(_headerIdentity);
            card.Add(row);
            rootVisualElement.Add(card);
        }

        private void BuildControlsCard()
        {
            _controlsCard = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Controls", "控制"));

            _refreshButton = CoCoEditorElements.CreatePrimaryButton(
                CoCoEditorLocalization.Text(
                    "Refresh Committed Snapshot", "刷新提交快照"),
                () => { PullAndRefresh(); });
            _refreshButton.style.marginRight = 6f;

            _suspendButton = new Button(() => SuspendRuntime())
            {
                text = CoCoEditorLocalization.Text("Suspend Runtime", "挂起运行时")
            };
            _suspendButton.style.marginRight = 6f;

            _resumeButton = new Button(() => ResumeRuntime())
            {
                text = CoCoEditorLocalization.Text("Resume Runtime", "恢复运行时")
            };
            _resumeButton.style.marginRight = 6f;

            _deltaField = new DoubleField(
                CoCoEditorLocalization.Text("Delta Time", "步进时间"))
            {
                value = _deltaTime,
                name = "delta-field"
            };
            _deltaField.RegisterValueChangedCallback(evt => _deltaTime = evt.newValue);
            _deltaField.style.width = 150f;

            _stepButton = new Button(() => StepOneTick())
            {
                text = CoCoEditorLocalization.Text(
                    "Run One Normal Tick", "单步一个普通 Tick")
            };

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.Add(_refreshButton);
            row.Add(_suspendButton);
            row.Add(_resumeButton);
            row.Add(_stepButton);
            row.Add(_deltaField);
            _controlsCard.Add(row);
            rootVisualElement.Add(_controlsCard);
        }

        private void BuildMetricsCard()
        {
            _metricsCard = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Committed State", "已提交状态"));
            var strip = new VisualElement();
            strip.AddToClassList("ccflow-host-metrics");
            _metricTick = AddMetric(strip, "Tick");
            _metricSeconds = AddMetric(strip, "Seconds");
            _metricSequence = AddMetric(strip, "Sequence");
            _metricLayers = AddMetric(strip, "Layers");
            _metricClaims = AddMetric(strip, "Claims");
            _metricsCard.Add(strip);
            rootVisualElement.Add(_metricsCard);
        }

        private static Label AddMetric(VisualElement strip, string label)
        {
            var metric = new VisualElement();
            metric.AddToClassList("ccflow-host-metric");
            var value = new Label("—")
            {
                name = "ccflow-metric-value",
                style = { whiteSpace = WhiteSpace.Normal }
            };
            value.AddToClassList("ccflow-host-metric__value");
            var name = new Label(label)
            {
                style = { whiteSpace = WhiteSpace.Normal }
            };
            name.AddToClassList("ccflow-host-metric__label");
            metric.Add(value);
            metric.Add(name);
            strip.Add(metric);
            return value;
        }

        private void BuildSnapshotCard()
        {
            _snapshotCard = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Snapshot", "快照"));

            _snapshotFreshnessRow = new VisualElement();
            _snapshotFreshnessRow.style.flexDirection = FlexDirection.Row;
            _snapshotFreshnessRow.style.marginBottom = 4f;
            _snapshotFreshnessBadge = new Label(string.Empty);
            _snapshotFreshnessBadge.AddToClassList("ccflow-muted");
            _snapshotFreshnessRow.Add(_snapshotFreshnessBadge);
            _snapshotCard.Add(_snapshotFreshnessRow);

            _snapshotList = new ListView
            {
                name = "snapshot-list",
                selectionType = SelectionType.None,
                virtualizationMethod =
                    CollectionVirtualizationMethod.DynamicHeight,
                makeItem = MakeSnapshotRow,
                bindItem = BindSnapshotRow
            };
            _snapshotList.style.flexGrow = 1f;
            _snapshotList.style.maxHeight = 320f;
            _snapshotCard.Add(_snapshotList);
            rootVisualElement.Add(_snapshotCard);
        }

        private static VisualElement MakeSnapshotRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ccflow-host-kv-row");
            var key = new Label { name = "ccflow-kv-key" };
            key.AddToClassList("ccflow-host-kv-row__key");
            var value = new Label { name = "ccflow-kv-value" };
            value.AddToClassList("ccflow-host-kv-row__value");
            value.style.whiteSpace = WhiteSpace.Normal;
            row.Add(key);
            row.Add(value);
            return row;
        }

        private void BindSnapshotRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _snapshotRowsCache.Count)
            {
                return;
            }

            CoCoDebuggerSnapshotRow data = _snapshotRowsCache[index];
            row.Q<Label>("ccflow-kv-key").text = data.Key;
            row.Q<Label>("ccflow-kv-value").text = data.Value;
        }

        private void BuildTraceCard()
        {
            _traceCard = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Trace", "Trace 历史"));

            _filterRow = new VisualElement();
            _filterRow.style.flexDirection = FlexDirection.Row;
            _filterRow.style.flexWrap = Wrap.Wrap;
            _filterRow.style.marginBottom = 4f;

            _filterToggles = new ToolbarToggle[3];
            for (int index = 0; index < _filterToggles.Length; index++)
            {
                int captured = index;
                var toggle = new ToolbarToggle
                {
                    text = TraceFilterLabels[index],
                    name = "trace-filter-" + index
                };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        SetTraceFilterMode(
                            (CoCoStateGraphHostTraceFilterMode)captured);
                    }
                    else if (_state.TraceFilterMode ==
                             (CoCoStateGraphHostTraceFilterMode)captured)
                    {
                        toggle.SetValueWithoutNotify(true);
                    }
                });
                _filterToggles[index] = toggle;
                _filterRow.Add(toggle);
            }

            _filterIdField = new TextField("ID")
            {
                name = "trace-filter-id",
                value = _state.TraceFilterText
            };
            _filterIdField.RegisterValueChangedCallback(evt =>
            {
                _state.SetTraceFilter(_state.TraceFilterMode, evt.newValue);
                MarkDirty();
            });
            _filterIdField.style.width = 280f;
            _filterRow.Add(_filterIdField);
            _traceCard.Add(_filterRow);

            _traceCountLabel = new Label(string.Empty);
            _traceCountLabel.AddToClassList("ccflow-muted");
            _traceCountLabel.style.whiteSpace = WhiteSpace.Normal;
            _traceCountLabel.style.marginBottom = 4f;
            _traceCard.Add(_traceCountLabel);

            _traceList = new ListView
            {
                name = "trace-list",
                selectionType = SelectionType.None,
                virtualizationMethod =
                    CollectionVirtualizationMethod.DynamicHeight,
                makeItem = MakeTraceRow,
                bindItem = BindTraceRow
            };
            _traceList.style.flexGrow = 1f;
            _traceList.style.maxHeight = 260f;
            _traceCard.Add(_traceList);
            rootVisualElement.Add(_traceCard);
        }

        private static VisualElement MakeTraceRow()
        {
            var row = new VisualElement();
            var label = new Label { name = "ccflow-trace-text" };
            label.AddToClassList("ccflow-host-trace-row");
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);
            return row;
        }

        private void BindTraceRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _traceRowsCache.Count)
            {
                return;
            }

            var label = row.Q<Label>("ccflow-trace-text");
            CoCoDebuggerTraceRow data = _traceRowsCache[index];
            label.text = data.Text;
            label.EnableInClassList(
                "ccflow-host-trace-group",
                data.IsGroupHeader);
        }

        private void BuildDiagnosticRow()
        {
            _diagnosticRow = new VisualElement { name = "ccflow-debugger-diagnostic" };
            rootVisualElement.Add(_diagnosticRow);
        }

        private void BuildEmptyState()
        {
            _emptyState = CoCoEditorElements.CreateEmptyState(
                CoCoEditorLocalization.Text(
                    "Runtime debugging becomes available for a live Play Mode Host",
                    "运行时调试在 Play 模式的活跃 Host 上可用"),
                CoCoEditorLocalization.Text(
                    "Select a CoCoStateGraphHost and enter Play Mode.",
                    "选择一个 CoCoStateGraphHost 并进入 Play 模式。"),
                CoCoEditorLocalization.Text(
                    "Keep Follow Selection on to inspect the Host under selection.",
                    "保持「跟随选择」开启即可检视当前选中的 Host。"));
            rootVisualElement.Add(_emptyState);
        }

        // ===== 数据流 =====

        private void RebindHostField()
        {
            _hostField.SetValueWithoutNotify(_host);
        }

        private void ObserveAndRefresh()
        {
            _state.ObserveIdentity(_host);
            RefreshNow();
        }

        private void TryFollowSelection()
        {
            GameObject selected = Selection.activeGameObject;
            _host = selected == null
                ? null
                : selected.GetComponentInParent<CoCoStateGraphHost>(true);
        }

        private void OnSelectionChange()
        {
            if (!_followSelection)
            {
                return;
            }

            TryFollowSelection();
            RebindHostField();
            ObserveAndRefresh();
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (Application.isPlaying && _host != null)
            {
                MarkDirty();
            }
        }

        private void PullAndRefresh()
        {
            _state.ObserveIdentity(_host);
            _state.TryRefresh(_host);
            _state.PullTrace(_host);
            RefreshNow();
        }

        // ===== 生命周期操作（语义保持；失败经 CoCoLog，D7） =====

        private void SuspendRuntime()
        {
            if (_host == null)
            {
                return;
            }

            if (!_host.TrySuspend(out CoCoDiagnostic diagnostic))
            {
                CoCoLog.Error("[StateGraph Debugger] Suspend failed: " +
                    diagnostic.Message);
            }

            PullAndRefresh();
        }

        private void ResumeRuntime()
        {
            if (_host == null)
            {
                return;
            }

            if (!_host.TryResume(out CoCoDiagnostic diagnostic))
            {
                CoCoLog.Error("[StateGraph Debugger] Resume failed: " +
                    diagnostic.Message);
            }

            PullAndRefresh();
        }

        private void StepOneTick()
        {
            if (_host == null)
            {
                return;
            }

            if (!_host.TryDebugStepWhileSuspended(_deltaTime, out CoCoDiagnostic diagnostic))
            {
                CoCoLog.Error("[StateGraph Debugger] One-tick step failed: " +
                    diagnostic.Message);
            }

            PullAndRefresh();
        }

        // ===== 过滤器 =====

        private void SetTraceFilterMode(CoCoStateGraphHostTraceFilterMode mode)
        {
            _state.SetTraceFilter(mode, _state.TraceFilterText);
            for (int index = 0; index < _filterToggles.Length; index++)
            {
                _filterToggles[index].SetValueWithoutNotify(
                    (int)mode == index);
            }

            _filterIdField.style.display =
                mode == CoCoStateGraphHostTraceFilterMode.All
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            MarkDirty();
        }

        // ===== 刷新（帧级合并 + generation guard） =====

        private void MarkDirty()
        {
            _pendingRefresh = true;
            if (_traceList == null || _scheduledRefresh != null)
            {
                return;
            }

            int capturedGeneration = _generation;
            _scheduledRefresh = rootVisualElement.schedule.Execute(() =>
            {
                _scheduledRefresh = null;
                if (capturedGeneration != _generation)
                {
                    if (_pendingRefresh && _traceList != null)
                    {
                        MarkDirty();
                    }

                    return;
                }

                if (_pendingRefresh)
                {
                    PullAndRefresh();
                }
            });
        }

        private void RefreshNow()
        {
            _pendingRefresh = false;
            if (_traceList == null)
            {
                return;
            }

            UpdateHeader();
            UpdateControls();
            UpdateMetrics();
            UpdateSnapshotList();
            UpdateTraceList();
            UpdateDiagnosticRow();
            UpdateEmptyState();
        }

        private void UpdateHeader()
        {
            bool live = _host != null && _host.HasLiveRuntime;
            if (_headerBadge != null)
            {
                var badgeText = _headerBadge.Q<Label>("ccflow-badge-text");
                if (badgeText != null)
                {
                    badgeText.text = _host == null
                        ? CoCoEditorLocalization.Text("No Host", "无宿主")
                        : _host.Lifecycle.ToString();
                }

                CoCoEditorElements.SetBadgeKind(
                    _headerBadge,
                    _host == null
                        ? CoCoEditorBadgeKind.Neutral
                        : LifecycleToBadgeKind(
                            _host.Lifecycle,
                            _host.Fault.IsFaulted));
            }

            if (_headerIdentity != null)
            {
                _headerIdentity.text = live && _host.GraphInstanceId.IsValid
                    ? _host.name + " · instance " + _host.GraphInstanceId
                    : _host == null ? string.Empty : _host.name;
            }
        }

        private void UpdateControls()
        {
            if (_controlsCard == null || _host == null)
            {
                if (_controlsCard != null)
                {
                    _controlsCard.SetEnabled(false);
                }

                return;
            }

            bool live = _host.HasLiveRuntime;
            _controlsCard.SetEnabled(live);
            if (!live)
            {
                return;
            }

            CoCoRuntimeLifecycleState lifecycle = _host.Lifecycle;
            _suspendButton.SetEnabled(
                lifecycle == CoCoRuntimeLifecycleState.Running &&
                !_host.Fault.IsFaulted);
            bool suspended = lifecycle == CoCoRuntimeLifecycleState.Suspended;
            _resumeButton.SetEnabled(suspended);
            _stepButton.SetEnabled(
                suspended &&
                _deltaTime > 0d &&
                !double.IsNaN(_deltaTime) &&
                !double.IsInfinity(_deltaTime));
            _refreshButton.SetEnabled(true);
        }

        private void UpdateMetrics()
        {
            if (_metricTick == null)
            {
                return;
            }

            CoCoStateGraphHostDebugSnapshot snapshot = _state.Snapshot;
            if (snapshot == null)
            {
                _metricTick.text =
                    _metricSeconds.text =
                    _metricSequence.text =
                    _metricLayers.text =
                    _metricClaims.text = "—";
                return;
            }

            _metricTick.text = snapshot.Tick.ToString();
            _metricSeconds.text = snapshot.Seconds.ToString("0.###");
            _metricSequence.text = snapshot.ExecutionSequence.ToString();
            _metricLayers.text = snapshot.LayerCount.ToString();
            _metricClaims.text = snapshot.ClaimCount.ToString();
        }

        private void UpdateSnapshotList()
        {
            if (_snapshotList == null)
            {
                return;
            }

            _snapshotRowsCache = _state.BuildSnapshotRows();
            _snapshotList.itemsSource = _snapshotRowsCache;
            _snapshotList.RefreshItems();

            if (_snapshotFreshnessBadge != null)
            {
                switch (_state.Freshness)
                {
                    case CoCoDebuggerSnapshotFreshness.RetainedStale:
                        _snapshotFreshnessRow.style.display = DisplayStyle.Flex;
                        _snapshotFreshnessBadge.text =
                            CoCoEditorLocalization.Text(
                                "retained last committed snapshot (latest refresh was rejected)",
                                "保留的上次提交快照（最近一次刷新被拒绝）");
                        _snapshotFreshnessBadge.style.color =
                            new Color(0.82f, 0.57f, 0.14f);
                        break;
                    case CoCoDebuggerSnapshotFreshness.Fresh:
                        _snapshotFreshnessRow.style.display = DisplayStyle.None;
                        break;
                    default:
                        _snapshotFreshnessRow.style.display =
                            _state.Snapshot == null
                                ? DisplayStyle.None
                                : DisplayStyle.Flex;
                        _snapshotFreshnessBadge.text = string.Empty;
                        break;
                }
            }
        }

        private void UpdateTraceList()
        {
            if (_traceList == null)
            {
                return;
            }

            _state.GetTraceCounts(
                _host,
                out int count,
                out int capacity,
                out ulong totalWritten,
                out int visible);
            _traceCountLabel.text = CoCoEditorLocalization.Text(
                $"Count {count}; Capacity {capacity}; Total Written {totalWritten}; " +
                    $"Visible {visible}",
                $"计数 {count}；容量 {capacity}；累计写入 {totalWritten}；" +
                    $"可见 {visible}");

            if (capacity == 0 && _host != null && _host.Trace == null)
            {
                _traceRowsCache =
                    new System.Collections.Generic.List<CoCoDebuggerTraceRow>
                    {
                        new CoCoDebuggerTraceRow(
                            true,
                            CoCoEditorLocalization.Text(
                                "Trace Capacity is 0 — stop the Host, set a positive " +
                                    "capacity, and restart to record history",
                                "Trace 容量为 0——停止 Host，设置正容量并重启以记录历史"))
                    };
                _traceList.itemsSource = _traceRowsCache;
                _traceList.RefreshItems();
                return;
            }

            if (!string.IsNullOrEmpty(_state.TraceFilterValidationMessage))
            {
                _traceRowsCache =
                    new System.Collections.Generic.List<CoCoDebuggerTraceRow>
                    {
                        new CoCoDebuggerTraceRow(
                            true,
                            _state.TraceFilterValidationMessage)
                    };
                _traceList.itemsSource = _traceRowsCache;
                _traceList.RefreshItems();
                return;
            }

            _traceRowsCache = _state.BuildTraceRows();
            _traceList.itemsSource = _traceRowsCache;
            _traceList.RefreshItems();
        }

        private void UpdateDiagnosticRow()
        {
            if (_diagnosticRow == null)
            {
                return;
            }

            _diagnosticRow.Clear();
            if (!_state.LastRefreshDiagnostic.IsError)
            {
                return;
            }

            _diagnosticRow.Add(CoCoEditorElements.CreateDiagnosticRow(
                _state.LastRefreshDiagnostic.Domain + "/" +
                    _state.LastRefreshDiagnostic.Code + ": " +
                    _state.LastRefreshDiagnostic.Message,
                CoCoEditorBadgeKind.Error));
        }

        private void UpdateEmptyState()
        {
            if (_emptyState == null)
            {
                return;
            }

            bool playing = Application.isPlaying;
            bool hasHost = _host != null;
            bool live = hasHost && _host.HasLiveRuntime;
            bool show = !playing || !hasHost || !live;

            _emptyState.style.display =
                show ? DisplayStyle.Flex : DisplayStyle.None;
            var mainList = rootVisualElement;
            _snapshotCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            _metricsCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            _traceCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            _controlsCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            mainList.MarkDirtyRepaint();
        }

        private static CoCoEditorBadgeKind LifecycleToBadgeKind(
            CoCoRuntimeLifecycleState lifecycle,
            bool faulted)
        {
            if (faulted)
            {
                return CoCoEditorBadgeKind.Error;
            }

            switch (lifecycle)
            {
                case CoCoRuntimeLifecycleState.Running:
                    return CoCoEditorBadgeKind.Success;
                case CoCoRuntimeLifecycleState.Suspended:
                    return CoCoEditorBadgeKind.Warning;
                default:
                    return CoCoEditorBadgeKind.Neutral;
            }
        }

        private void OnLanguageChanged()
        {
            if (rootVisualElement == null)
            {
                return;
            }

            _scheduledRefresh?.Pause();
            _scheduledRefresh = null;
            _periodicPoll?.Pause();
            _periodicPoll = null;
            rootVisualElement.Clear();
            CreateGUI();
        }
    }
}
