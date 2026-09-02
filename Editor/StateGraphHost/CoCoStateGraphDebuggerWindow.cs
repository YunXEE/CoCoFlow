using System;
using System.Collections.Generic;
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
    /// guard + 帧级合并刷新；控件经 SyncControlsFromState 与数据层单向同步。
    /// </summary>
    internal sealed class CoCoStateGraphDebuggerWindow : EditorWindow
    {
        private const string ModuleUssPath =
            "Packages/com.yunxee.cocoflow/Editor/StateGraphHost/CoCoStateGraphHostEditor.uss";

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
        private VisualElement _snapshotSections;
        private ListView _layerList;
        private ListView _claimList;
        private VisualElement _traceCard;
        private ToolbarToggle[] _filterToggles;
        private TextField _filterIdField;
        private Label _traceCountLabel;
        private ListView _traceList;
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
            // 打开前先探查当前 Selection（原行为），再进入跟随模式。
            window.TryFollowSelection();
            window.SelectHost(window._host, followSelection: true);
            window.Show();
        }

        internal static void Open(CoCoStateGraphHost host)
        {
            CoCoStateGraphDebuggerWindow window =
                GetWindow<CoCoStateGraphDebuggerWindow>();
            window.titleContent = new GUIContent("StateGraph Debugger");
            window.minSize = new Vector2(420f, 320f);
            window.SelectHost(host, followSelection: false);
            window.Show();
            window.Focus();
        }

        private readonly System.Collections.Generic.Dictionary<string, bool>
            _foldoutStates = new System.Collections.Generic.Dictionary<string, bool>();

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

            // 内容区（ScrollView：窄窗口下所有卡可达，P2-04）。
            var scroll = new ScrollView { name = "ccflow-debugger-scroll" };
            scroll.Add(BuildHeaderCard());
            _controlsCard = BuildControlsCard();
            scroll.Add(_controlsCard);
            _metricsCard = BuildMetricsCard();
            scroll.Add(_metricsCard);
            _snapshotCard = BuildSnapshotCard();
            scroll.Add(_snapshotCard);
            _traceCard = BuildTraceCard();
            scroll.Add(_traceCard);
            _diagnosticRow = new VisualElement { name = "ccflow-debugger-diagnostic" };
            scroll.Add(_diagnosticRow);
            root.Add(scroll);

            BuildEmptyState();

            SyncHostField();
            SyncControlsFromState();
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
                SelectHost(evt.newValue as CoCoStateGraphHost, _followSelection));
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
                    SelectHost(_host, followSelection: true);
                }
            });
            toolbar.Add(_followToggle);
            rootVisualElement.Add(toolbar);
        }

        private VisualElement BuildHeaderCard()
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
            return card;
        }

        private VisualElement BuildControlsCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
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
            _deltaField.RegisterValueChangedCallback(evt =>
            {
                _deltaTime = evt.newValue;
                UpdateControls(); // 即时反映 step 可用性（P2-01）
            });
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
            card.Add(row);
            return card;
        }

        private VisualElement BuildMetricsCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Committed State", "已提交状态"));
            var strip = new VisualElement();
            strip.AddToClassList("ccflow-host-metrics");
            _metricTick = AddMetric(
                strip, CoCoEditorLocalization.Text("Tick", "Tick"));
            _metricSeconds = AddMetric(
                strip, CoCoEditorLocalization.Text("Seconds", "秒"));
            _metricSequence = AddMetric(
                strip, CoCoEditorLocalization.Text("Sequence", "序号"));
            _metricLayers = AddMetric(
                strip, CoCoEditorLocalization.Text("Layers", "层"));
            _metricClaims = AddMetric(
                strip, CoCoEditorLocalization.Text("Claims", "占用"));
            card.Add(strip);
            return card;
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

        private VisualElement BuildSnapshotCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Snapshot", "快照"));

            _snapshotFreshnessRow = new VisualElement();
            _snapshotFreshnessRow.style.flexDirection = FlexDirection.Row;
            _snapshotFreshnessRow.style.marginBottom = 4f;
            _snapshotFreshnessBadge = new Label(string.Empty);
            _snapshotFreshnessBadge.AddToClassList("ccflow-muted");
            _snapshotFreshnessRow.Add(_snapshotFreshnessBadge);
            card.Add(_snapshotFreshnessRow);

            // 分区折叠组（P2-04：Section 可见 + 信息层级）。
            _snapshotSections = new VisualElement { name = "ccflow-snapshot-sections" };
            card.Add(_snapshotSections);

            var layersFoldout = new Foldout
            {
                text = CoCoEditorLocalization.Text("Layers", "层"),
                value = true
            };
            layersFoldout.AddToClassList("ccflow-foldout");
            _layerList = new ListView
            {
                name = "layer-list",
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = MakeLayerRow,
                bindItem = BindLayerRow
            };
            _layerList.style.maxHeight = 200f;
            layersFoldout.Add(_layerList);
            card.Add(layersFoldout);

            var claimsFoldout = new Foldout
            {
                text = CoCoEditorLocalization.Text("Claims", "占用"),
                value = true
            };
            claimsFoldout.AddToClassList("ccflow-foldout");
            _claimList = new ListView
            {
                name = "claim-list",
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = MakeClaimRow,
                bindItem = BindClaimRow
            };
            _claimList.style.maxHeight = 140f;
            claimsFoldout.Add(_claimList);
            card.Add(claimsFoldout);
            return card;
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

        private static VisualElement MakeLayerRow()
        {
            var row = new VisualElement();
            var label = new Label { name = "ccflow-layer-text" };
            label.AddToClassList("ccflow-host-trace-row");
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);
            return row;
        }

        private void BindLayerRow(VisualElement row, int index)
        {
            var label = row.Q<Label>("ccflow-layer-text");
            if (index < 0 || index >= _layerRowsCache.Count)
            {
                label.text = string.Empty;
                return;
            }

            CoCoDebuggerLayerRow data = _layerRowsCache[index];
            label.text = data.LayerId +
                " | " + CoCoEditorLocalization.Text("Winner", "胜出") + " " +
                data.Winner +
                (string.IsNullOrEmpty(data.ActiveStates) ? string.Empty : "\n" +
                    data.ActiveStates);
        }

        private static VisualElement MakeClaimRow()
        {
            var row = new VisualElement();
            var label = new Label { name = "ccflow-claim-text" };
            label.AddToClassList("ccflow-host-trace-row");
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);
            return row;
        }

        private void BindClaimRow(VisualElement row, int index)
        {
            var label = row.Q<Label>("ccflow-claim-text");
            label.text = index >= 0 && index < _claimRowsCache.Count
                ? _claimRowsCache[index]
                : string.Empty;
        }

        private VisualElement BuildTraceCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Trace", "Trace 历史"));

            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.flexWrap = Wrap.Wrap;
            filterRow.style.marginBottom = 4f;

            _filterToggles = new ToolbarToggle[3];
            for (int index = 0; index < _filterToggles.Length; index++)
            {
                int captured = index;
                var toggle = new ToolbarToggle
                {
                    text = TraceFilterLabel(
                        (CoCoStateGraphHostTraceFilterMode)index),
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
                filterRow.Add(toggle);
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
            filterRow.Add(_filterIdField);
            card.Add(filterRow);

            _traceCountLabel = new Label(string.Empty);
            _traceCountLabel.AddToClassList("ccflow-muted");
            _traceCountLabel.style.whiteSpace = WhiteSpace.Normal;
            _traceCountLabel.style.marginBottom = 4f;
            card.Add(_traceCountLabel);

            _traceList = new ListView
            {
                name = "trace-list",
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = MakeTraceRow,
                bindItem = BindTraceRow
            };
            _traceList.style.maxHeight = 260f;
            card.Add(_traceList);
            return card;
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
            var label = row.Q<Label>("ccflow-trace-text");
            if (index < 0 || index >= _traceRowsCache.Count)
            {
                label.text = string.Empty;
                return;
            }

            CoCoDebuggerTraceRow data = _traceRowsCache[index];
            label.text = data.Text;
            label.EnableInClassList(
                "ccflow-host-trace-group",
                data.IsGroupHeader);
        }

        private void BuildEmptyState()
        {
            _emptyState = new VisualElement { name = "ccflow-debugger-empty" };
            _emptyState.AddToClassList("ccflow-empty");
            rootVisualElement.Add(_emptyState);
        }

        private void RebuildEmptyState(string title, string message, string firstStep)
        {
            _emptyState.Clear();
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("ccflow-empty__title");
            var messageLabel = new Label(message);
            messageLabel.AddToClassList("ccflow-empty__message");
            var firstStepLabel = new Label(firstStep);
            firstStepLabel.AddToClassList("ccflow-empty__first-step");
            _emptyState.Add(titleLabel);
            _emptyState.Add(messageLabel);
            _emptyState.Add(firstStepLabel);
        }

        // ===== 宿主选择（唯一路径：Open / ObjectField / Follow / Selection） =====

        private void SelectHost(CoCoStateGraphHost host, bool followSelection)
        {
            _host = host;
            _followSelection = followSelection;
            SyncHostField();
            _state.ObserveIdentity(_host);
            PullAndRefresh();
        }

        private void SyncHostField()
        {
            _hostField?.SetValueWithoutNotify(_host);
            _followToggle?.SetValueWithoutNotify(_followSelection);
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
            SelectHost(_host, followSelection: true);
            Repaint();
        }

        // 注：Play 期刷新由 300ms 周期轮询单路驱动（避免 OnInspectorUpdate
        // 双路触发快照拷贝与 UI 重建，见交付审计线程处置）。

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

            if (!_host.TryDebugStepWhileSuspended(
                    _deltaTime, out CoCoDiagnostic diagnostic))
            {
                CoCoLog.Error("[StateGraph Debugger] One-tick step failed: " +
                    diagnostic.Message);
            }

            PullAndRefresh();
        }

        // ===== 过滤器（模型 → 控件单向同步） =====

        private static string TraceFilterLabel(
            CoCoStateGraphHostTraceFilterMode mode)
        {
            switch (mode)
            {
                case CoCoStateGraphHostTraceFilterMode.StateId:
                    return CoCoEditorLocalization.Text("State ID", "State ID");
                case CoCoStateGraphHostTraceFilterMode.TransitionId:
                    return CoCoEditorLocalization.Text(
                        "Transition ID", "Transition ID");
                default:
                    return CoCoEditorLocalization.Text("All", "全部");
            }
        }

        private void SetTraceFilterMode(CoCoStateGraphHostTraceFilterMode mode)
        {
            _state.SetTraceFilter(mode, _state.TraceFilterText);
            SyncControlsFromState();
            MarkDirty();
        }

        /// <summary>控件 ← 数据层单向同步（身份重置/delta 变化后调用，P2-01）。</summary>
        private void SyncControlsFromState()
        {
            if (_filterToggles == null)
            {
                return;
            }

            for (int index = 0; index < _filterToggles.Length; index++)
            {
                _filterToggles[index].SetValueWithoutNotify(
                    (int)_state.TraceFilterMode == index);
            }

            _filterIdField.SetValueWithoutNotify(_state.TraceFilterText);
            _filterIdField.style.display =
                _state.TraceFilterMode == CoCoStateGraphHostTraceFilterMode.All
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
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

        private readonly List<CoCoDebuggerLayerRow> _layerRowsCache = new();
        private readonly List<string> _claimRowsCache = new();
        private readonly List<CoCoDebuggerTraceRow> _traceRowsCache = new();

        private void RefreshNow()
        {
            _pendingRefresh = false;
            if (_traceList == null)
            {
                return;
            }

            SyncControlsFromState();
            UpdateHeader();
            UpdateControls();
            UpdateMetrics();
            UpdateSnapshotCard();
            UpdateTraceList();
            UpdateDiagnosticRow();
            UpdateEmptyState();
        }

        private void UpdateHeader()
        {
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
                bool live = _host != null && _host.HasLiveRuntime;
                _headerIdentity.text = live && _host.GraphInstanceId.IsValid
                    ? _host.name + " · instance " + _host.GraphInstanceId
                    : _host == null ? string.Empty : _host.name;
            }
        }

        private void UpdateControls()
        {
            if (_controlsCard == null)
            {
                return;
            }

            if (_host == null || !_host.HasLiveRuntime)
            {
                _controlsCard.SetEnabled(false);
                return;
            }

            _controlsCard.SetEnabled(true);
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

        private void UpdateSnapshotCard()
        {
            if (_snapshotSections == null)
            {
                return;
            }

            _snapshotSections.Clear();
            List<CoCoDebuggerSnapshotSection> sections =
                _state.BuildSnapshotSections();
            for (int index = 0; index < sections.Count; index++)
            {
                CoCoDebuggerSnapshotSection section = sections[index];
                var foldout = new Foldout
                {
                    text = section.Title,
                    value = !_foldoutStates.TryGetValue(
                        section.Title,
                        out bool collapsed) || !collapsed
                };
                foldout.AddToClassList("ccflow-foldout");
                string capturedTitle = section.Title;
                foldout.RegisterValueChangedCallback(evt =>
                    _foldoutStates[capturedTitle] = !evt.newValue);
                IReadOnlyList<CoCoDebuggerSnapshotRow> rows = section.Rows;
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    VisualElement row = MakeSnapshotRow();
                    row.Q<Label>("ccflow-kv-key").text = rows[rowIndex].Key;
                    row.Q<Label>("ccflow-kv-value").text = rows[rowIndex].Value;
                    foldout.Add(row);
                }

                _snapshotSections.Add(foldout);
            }

            _layerRowsCache.Clear();
            _layerRowsCache.AddRange(_state.BuildLayerRows());
            _layerList.itemsSource = _layerRowsCache;
            _layerList.RefreshItems();

            _claimRowsCache.Clear();
            _claimRowsCache.AddRange(_state.BuildClaimRows());
            _claimList.itemsSource = _claimRowsCache;
            _claimList.RefreshItems();

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
                    default:
                        _snapshotFreshnessRow.style.display = DisplayStyle.None;
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

            if (_host != null && _host.Trace == null)
            {
                _traceRowsCache.Clear();
                _traceRowsCache.Add(new CoCoDebuggerTraceRow(
                    true,
                    CoCoEditorLocalization.Text(
                        "Trace Capacity is 0 — stop the Host, set a positive " +
                            "capacity, and restart to record history",
                        "Trace 容量为 0——停止 Host，设置正容量并重启以记录历史")));
                _traceList.itemsSource = _traceRowsCache;
                _traceList.RefreshItems();
                return;
            }

            if (!string.IsNullOrEmpty(_state.TraceFilterValidationMessage))
            {
                _traceRowsCache.Clear();
                _traceRowsCache.Add(new CoCoDebuggerTraceRow(
                    true,
                    _state.TraceFilterValidationMessage));
                _traceList.itemsSource = _traceRowsCache;
                _traceList.RefreshItems();
                return;
            }

            _traceRowsCache.Clear();
            _traceRowsCache.AddRange(_state.BuildTraceRows());
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

        /// <summary>空状态区分原因（未运行 / 无宿主 / 宿主未启动，P2-04）。</summary>
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
            _snapshotCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            _metricsCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            _traceCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            _controlsCard.style.display =
                show ? DisplayStyle.None : DisplayStyle.Flex;
            if (!show)
            {
                return;
            }

            if (!playing)
            {
                RebuildEmptyState(
                    CoCoEditorLocalization.Text(
                        "Runtime debugging becomes available in Play Mode",
                        "运行时调试在 Play 模式可用"),
                    CoCoEditorLocalization.Text(
                        "The committed snapshot and Trace reflect a live Host.",
                        "提交快照与 Trace 反映运行中的 Host。"),
                    CoCoEditorLocalization.Text(
                        "Enter Play Mode with a Host in the scene.",
                        "场景中放置 Host 并进入 Play 模式。"));
                return;
            }

            if (!hasHost)
            {
                RebuildEmptyState(
                    CoCoEditorLocalization.Text(
                        "No Host selected", "未选择宿主"),
                    CoCoEditorLocalization.Text(
                        "Pick a CoCoStateGraphHost to observe its committed state.",
                        "选择一个 CoCoStateGraphHost 以观察其已提交状态。"),
                    CoCoEditorLocalization.Text(
                        "Keep Follow Selection on to inspect the Host under selection.",
                        "保持「跟随选择」开启即可检视当前选中的 Host。"));
                return;
            }

            RebuildEmptyState(
                CoCoEditorLocalization.Text(
                    "Host runtime is not live", "宿主运行时未启动"),
                CoCoEditorLocalization.Text(
                    "The Host has no live runtime yet (not started, stopped, or faulted).",
                    "该 Host 尚无活跃运行时（未启动、已停止或已故障）。"),
                CoCoEditorLocalization.Text(
                    "Check Auto Start / driver settings or start the Host, then refresh.",
                    "检查自动启动/驱动设置或启动 Host 后刷新。"));
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
