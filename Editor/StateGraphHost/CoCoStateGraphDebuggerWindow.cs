using System;
using System.Collections.Generic;
using System.Globalization;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraphHost
{
    /// <summary>
    /// Serializable identity for a scene Host. The live component instance is
    /// intentionally not retained across Play Mode scene reloads.
    /// </summary>
    [Serializable]
    internal sealed class CoCoStateGraphDebuggerTargetLocator
    {
        [SerializeField] private string scenePath;
        [SerializeField] private string sceneName;
        [SerializeField] private int[] siblingIndices;
        [SerializeField] private string[] hierarchyNames;
        [SerializeField] private int hostComponentIndex;

        internal bool IsValid =>
            siblingIndices != null &&
            hierarchyNames != null &&
            siblingIndices.Length > 0 &&
            siblingIndices.Length == hierarchyNames.Length;

        internal static bool TryCapture(
            CoCoStateGraphHost host,
            out CoCoStateGraphDebuggerTargetLocator locator)
        {
            locator = null;
            if (host == null || host.transform == null)
            {
                return false;
            }

            Scene scene = host.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            var transforms = new List<Transform>();
            for (Transform current = host.transform;
                 current != null;
                 current = current.parent)
            {
                transforms.Add(current);
            }

            transforms.Reverse();
            var indices = new int[transforms.Count];
            var names = new string[transforms.Count];
            for (int index = 0; index < transforms.Count; index++)
            {
                indices[index] = transforms[index].GetSiblingIndex();
                names[index] = transforms[index].name;
            }

            CoCoStateGraphHost[] hosts =
                host.gameObject.GetComponents<CoCoStateGraphHost>();
            int componentIndex = Array.IndexOf(hosts, host);
            if (componentIndex < 0)
            {
                return false;
            }

            locator = new CoCoStateGraphDebuggerTargetLocator
            {
                scenePath = scene.path,
                sceneName = scene.name,
                siblingIndices = indices,
                hierarchyNames = names,
                hostComponentIndex = componentIndex
            };
            return true;
        }

        internal bool TryResolve(out CoCoStateGraphHost host)
        {
            host = null;
            if (!IsValid || !TryResolveScene(out Scene scene))
            {
                return false;
            }

            Transform current = ResolveRoot(scene);
            if (current == null)
            {
                return false;
            }

            for (int depth = 1; depth < siblingIndices.Length; depth++)
            {
                current = ResolveChild(
                    current,
                    siblingIndices[depth],
                    hierarchyNames[depth]);
                if (current == null)
                {
                    return false;
                }
            }

            CoCoStateGraphHost[] hosts =
                current.GetComponents<CoCoStateGraphHost>();
            if (hostComponentIndex < 0 || hostComponentIndex >= hosts.Length)
            {
                return false;
            }

            host = hosts[hostComponentIndex];
            return host != null;
        }

        private bool TryResolveScene(out Scene scene)
        {
            scene = default;
            if (!string.IsNullOrEmpty(scenePath))
            {
                for (int index = 0;
                     index < SceneManager.sceneCount;
                     index++)
                {
                    Scene candidate = SceneManager.GetSceneAt(index);
                    if (candidate.isLoaded && candidate.path == scenePath)
                    {
                        scene = candidate;
                        return true;
                    }
                }
            }

            Scene nameMatch = default;
            int nameMatchCount = 0;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (!candidate.isLoaded || candidate.name != sceneName)
                {
                    continue;
                }

                nameMatch = candidate;
                nameMatchCount++;
            }

            if (nameMatchCount != 1)
            {
                return false;
            }

            scene = nameMatch;
            return true;
        }

        private Transform ResolveRoot(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            int expectedIndex = siblingIndices[0];
            string expectedName = hierarchyNames[0];
            if (expectedIndex >= 0 &&
                expectedIndex < roots.Length &&
                roots[expectedIndex].name == expectedName)
            {
                return roots[expectedIndex].transform;
            }

            Transform match = null;
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name != expectedName)
                {
                    continue;
                }

                if (match != null)
                {
                    return null;
                }

                match = roots[index].transform;
            }

            return match;
        }

        private static Transform ResolveChild(
            Transform parent,
            int expectedIndex,
            string expectedName)
        {
            if (expectedIndex >= 0 &&
                expectedIndex < parent.childCount)
            {
                Transform expected = parent.GetChild(expectedIndex);
                if (expected.name == expectedName)
                {
                    return expected;
                }
            }

            Transform match = null;
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (child.name != expectedName)
                {
                    continue;
                }

                if (match != null)
                {
                    return null;
                }

                match = child;
            }

            return match;
        }
    }

    /// <summary>
    /// Read-only inspector for one concrete StateGraphHost instance. The
    /// window renders only the current committed frame, its Temporal ring, and
    /// an existing persisted frame.
    /// </summary>
    internal sealed class CoCoStateGraphDebuggerWindow : EditorWindow
    {
        private const string ModuleUssPath =
            "Packages/com.yunxee.cocoflow/Editor/StateGraphHost/CoCoStateGraphHostEditor.uss";
        private const long PollIntervalMilliseconds = 250;
        private const double PersistencePollSeconds = 1.5d;

        private static readonly Color SceneMarkerOuterColor =
            new Color(0.035f, 0.11f, 0.18f, 0.98f);
        private static readonly Color SceneMarkerColor =
            new Color(0.18f, 0.68f, 1f, 1f);
        private static readonly Color SceneMarkerCenterColor =
            new Color(0.92f, 0.98f, 1f, 1f);

        private readonly CoCoStateGraphHostDebuggerState _state =
            new CoCoStateGraphHostDebuggerState();

        [NonSerialized] private CoCoStateGraphHost _host;
        [SerializeField] private CoCoStateGraphDebuggerTargetLocator targetLocator;
        [SerializeField] private bool followSelection = true;
        [SerializeField] private bool showSceneMarker = true;
        [NonSerialized] private bool _rebindScheduled;
        private double _nextPersistencePoll;
        private IVisualElementScheduledItem _poll;

        private ObjectField _hostField;
        private ToolbarToggle _followToggle;
        private ToolbarToggle _sceneMarkerToggle;
        private VisualElement _hostBadge;
        private Label _hostIdentity;
        private VisualElement _emptyState;
        private VisualElement _currentCard;
        private VisualElement _currentBadge;
        private VisualElement _currentRows;
        private VisualElement _activeStates;
        private VisualElement _ringCard;
        private Label _ringStatus;
        private CoCoTemporalRingElement _ring;
        private Label _selectedFrameTitle;
        private VisualElement _selectedFrameRows;
        private VisualElement _persistedCard;
        private VisualElement _persistedRows;
        private VisualElement _diagnostics;

        [MenuItem("CoCoFlow/StateGraph Debugger")]
        internal static void OpenFromMenu()
        {
            CoCoStateGraphDebuggerWindow window =
                GetWindow<CoCoStateGraphDebuggerWindow>();
            window.titleContent = new GUIContent("StateGraph Debugger");
            window.minSize = new Vector2(420f, 420f);
            window.followSelection = true;
            window.TryFollowSelection();
            window.Show();
        }

        internal static void Open(CoCoStateGraphHost host)
        {
            CoCoStateGraphDebuggerWindow window =
                GetWindow<CoCoStateGraphDebuggerWindow>();
            window.titleContent = new GUIContent("StateGraph Debugger");
            window.minSize = new Vector2(420f, 420f);
            window.followSelection = false;
            window.SelectHost(host);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged;
            Selection.selectionChanged += OnEditorSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SceneView.duringSceneGui += DrawSceneMarker;
            ScheduleTargetRebind();
        }

        private void OnDisable()
        {
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged;
            Selection.selectionChanged -= OnEditorSelectionChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            SceneView.duringSceneGui -= DrawSceneMarker;
            EditorApplication.delayCall -= RebindAfterModeChange;
            _rebindScheduled = false;
            _poll?.Pause();
            _poll = null;
            SceneView.RepaintAll();
        }

        private void CreateGUI()
        {
            _poll?.Pause();
            _poll = null;

            VisualElement root = rootVisualElement;
            root.Clear();
            CoCoEditorElements.ApplyTheme(root);
            ApplyModuleTheme(root);
            BuildToolbar(root);

            var scroll = new ScrollView { name = "ccflow-debugger-scroll" };
            scroll.Add(BuildHostHeader());
            _emptyState = new VisualElement
            {
                name = "ccflow-debugger-empty"
            };
            scroll.Add(_emptyState);
            _currentCard = BuildCurrentCard();
            scroll.Add(_currentCard);
            _ringCard = BuildRingCard();
            scroll.Add(_ringCard);
            _persistedCard = BuildPersistedCard();
            scroll.Add(_persistedCard);
            _diagnostics = new VisualElement
            {
                name = "ccflow-debugger-diagnostics"
            };
            scroll.Add(_diagnostics);
            root.Add(scroll);

            if (!(followSelection && TryFollowSelection()))
            {
                TryRebindTarget();
            }

            SyncToolbar();
            RefreshNow(includePersistence: true);
            _poll = root.schedule.Execute(Poll).Every(PollIntervalMilliseconds);
        }

        private void OnLanguageChanged()
        {
            CreateGUI();
        }

        private void OnEditorSelectionChanged()
        {
            if (!followSelection)
            {
                return;
            }

            TryFollowSelection();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.ExitingEditMode ||
                stateChange == PlayModeStateChange.ExitingPlayMode)
            {
                InvalidateLiveHost();
                return;
            }

            if (stateChange == PlayModeStateChange.EnteredEditMode ||
                stateChange == PlayModeStateChange.EnteredPlayMode)
            {
                ScheduleTargetRebind();
            }
        }

        private static void ApplyModuleTheme(VisualElement root)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ModuleUssPath);
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        private void BuildToolbar(VisualElement root)
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("ccflow-toolbar");

            _hostField = new ObjectField
            {
                allowSceneObjects = true,
                objectType = typeof(UnityEngine.Object),
                tooltip = CoCoEditorLocalization.Text(
                    "Assign a Host GameObject or CoCoStateGraphHost component.",
                    "拖入 Host GameObject 或 CoCoStateGraphHost 组件。"),
                name = "host-field"
            };
            _hostField.style.flexGrow = 1f;
            _hostField.RegisterValueChangedCallback(OnHostFieldChanged);
            toolbar.Add(_hostField);

            _followToggle = new ToolbarToggle
            {
                text = CoCoEditorLocalization.Text(
                    "Follow Selection",
                    "跟随选择"),
                name = "follow-toggle"
            };
            _followToggle.RegisterValueChangedCallback(evt =>
            {
                followSelection = evt.newValue;
                if (followSelection)
                {
                    TryFollowSelection();
                }
            });
            toolbar.Add(_followToggle);

            _sceneMarkerToggle = new ToolbarToggle
            {
                text = CoCoEditorLocalization.Text(
                    "Scene Marker",
                    "场景标记"),
                tooltip = CoCoEditorLocalization.Text(
                    "Show the analyzed Host marker in the Scene view.",
                    "在 Scene 窗口显示当前分析 Host 的标记。"),
                name = "scene-marker-toggle"
            };
            _sceneMarkerToggle.RegisterValueChangedCallback(evt =>
            {
                showSceneMarker = evt.newValue;
                SceneView.RepaintAll();
            });
            toolbar.Add(_sceneMarkerToggle);

            var refresh = new ToolbarButton(() =>
                RefreshNow(includePersistence: true))
            {
                text = CoCoEditorLocalization.Text("Refresh", "刷新"),
                name = "refresh-button"
            };
            toolbar.Add(refresh);
            root.Add(toolbar);
        }

        private VisualElement BuildHostHeader()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text(
                    "Host Temporal Debugger",
                    "Host 时间调试器"));
            var subtitle = new Label(CoCoEditorLocalization.Text(
                "Read-only view of one scene Host: current frame, Temporal ring, and persisted frame.",
                "只读观察一个场景 Host：当前帧、时间环与持久化帧。"));
            subtitle.AddToClassList("ccflow-muted");
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            card.Add(subtitle);

            var row = new VisualElement();
            row.AddToClassList("ccflow-debugger-host-row");
            _hostBadge = CoCoEditorElements.CreateBadge(
                CoCoEditorLocalization.Text("No Host", "无 Host"),
                CoCoEditorBadgeKind.Neutral);
            row.Add(_hostBadge);
            _hostIdentity = new Label { name = "host-identity" };
            _hostIdentity.AddToClassList("ccflow-debugger-host-identity");
            row.Add(_hostIdentity);
            card.Add(row);
            return card;
        }

        private VisualElement BuildCurrentCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text(
                    "1 · Current committed frame",
                    "1 · 当前提交帧"));

            _currentBadge = CoCoEditorElements.CreateBadge(
                CoCoEditorLocalization.Text("Fresh", "最新"),
                CoCoEditorBadgeKind.Success);
            card.Add(_currentBadge);

            _currentRows = new VisualElement
            {
                name = "current-frame-rows"
            };
            card.Add(_currentRows);

            Label statesTitle = CoCoEditorElements.CreateHeading(
                CoCoEditorLocalization.Text(
                    "Active States",
                    "当前 Active State"));
            statesTitle.style.marginTop = 10f;
            card.Add(statesTitle);
            _activeStates = new VisualElement
            {
                name = "active-state-rows"
            };
            card.Add(_activeStates);
            return card;
        }

        private VisualElement BuildRingCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text(
                    "2 · Temporal ring",
                    "2 · 时间环"));
            _ringStatus = new Label();
            _ringStatus.AddToClassList("ccflow-muted");
            _ringStatus.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_ringStatus);

            _ring = new CoCoTemporalRingElement
            {
                name = "temporal-ring"
            };
            _ring.DepthSelected += depth =>
            {
                _state.SelectDepth(depth);
                RenderRingSelection();
            };
            card.Add(_ring);

            _selectedFrameTitle = CoCoEditorElements.CreateHeading(string.Empty);
            card.Add(_selectedFrameTitle);
            _selectedFrameRows = new VisualElement
            {
                name = "selected-frame-rows"
            };
            card.Add(_selectedFrameRows);
            return card;
        }

        private VisualElement BuildPersistedCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text(
                    "3 · Persisted frame",
                    "3 · 持久化帧"));
            var note = new Label(CoCoEditorLocalization.Text(
                "Newest compatible frame already written to a standard save slot.",
                "标准存档槽中最近一次成功写盘且兼容的帧。"));
            note.AddToClassList("ccflow-muted");
            note.style.whiteSpace = WhiteSpace.Normal;
            card.Add(note);
            _persistedRows = new VisualElement
            {
                name = "persisted-frame-rows"
            };
            card.Add(_persistedRows);
            return card;
        }

        private void Poll()
        {
            if (_host == null)
            {
                TryRebindTarget();
                return;
            }

            bool includePersistence =
                EditorApplication.timeSinceStartup >= _nextPersistencePoll;
            RefreshNow(includePersistence);
        }

        private void RefreshNow(bool includePersistence)
        {
            _state.ObserveIdentity(_host);
            if (_host != null && Application.isPlaying)
            {
                _state.TryRefresh(_host);
                if (includePersistence && _state.Snapshot != null)
                {
                    _state.TryRefreshPersistence(_host);
                    _nextPersistencePoll =
                        EditorApplication.timeSinceStartup +
                        PersistencePollSeconds;
                }
            }

            if (_emptyState != null)
            {
                Render();
            }

            SceneView.RepaintAll();
        }

        private void Render()
        {
            if (_emptyState == null)
            {
                return;
            }

            SyncToolbar();
            RenderHostHeader();
            _diagnostics?.Clear();

            if (_host == null)
            {
                ShowEmptyState(
                    CoCoEditorLocalization.Text(
                        "Select a scene Host",
                        "选择场景 Host"),
                    CoCoEditorLocalization.Text(
                        "The debugger observes a concrete CoCoStateGraphHost instance, not a StateGraph asset.",
                        "Debugger 观察的是具体 CoCoStateGraphHost 实例，不是 StateGraph 资产。"),
                    CoCoEditorLocalization.Text(
                        "Select a Host in the Hierarchy or assign it above.",
                        "在 Hierarchy 选择 Host，或在上方指定。"));
                SetDataCardsVisible(false);
                return;
            }

            if (_state.Snapshot == null)
            {
                ShowEmptyState(
                    CoCoEditorLocalization.Text(
                        "Host is not at a readable committed boundary",
                        "Host 尚无可读取的提交边界"),
                    CoCoEditorLocalization.Text(
                        "Current frame and Temporal history exist only while this Host has a live idle runtime.",
                        "当前帧和时间历史仅在该 Host 具有空闲的 live runtime 时存在。"),
                    CoCoEditorLocalization.Text(
                        "Enter Play Mode and start this Host.",
                        "进入 Play Mode 并启动该 Host。"));
                SetDataCardsVisible(false);
                return;
            }

            _emptyState.style.display = DisplayStyle.None;
            _currentCard.style.display = DisplayStyle.Flex;
            _ringCard.style.display = DisplayStyle.Flex;
            _persistedCard.style.display = _state.HasPersistedFrame
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            RenderCurrentFrame();
            RenderRing();
            RenderPersistedFrame();
            RenderDiagnostics();
        }

        private void RenderHostHeader()
        {
            if (_host == null)
            {
                SetBadge(
                    _hostBadge,
                    CoCoEditorLocalization.Text("No Host", "无 Host"),
                    CoCoEditorBadgeKind.Neutral);
                _hostIdentity.text = string.Empty;
                return;
            }

            CoCoRuntimeLifecycleState lifecycle = _host.Lifecycle;
            CoCoEditorBadgeKind kind =
                lifecycle == CoCoRuntimeLifecycleState.Running ||
                lifecycle == CoCoRuntimeLifecycleState.Suspended
                    ? CoCoEditorBadgeKind.Success
                    : CoCoEditorBadgeKind.Neutral;
            SetBadge(_hostBadge, lifecycle.ToString(), kind);
            _hostIdentity.text = GetHierarchyPath(_host.transform);
        }

        private void RenderCurrentFrame()
        {
            bool stale =
                _state.Freshness == CoCoDebuggerSnapshotFreshness.RetainedStale;
            SetBadge(
                _currentBadge,
                stale
                    ? CoCoEditorLocalization.Text(
                        "Retained stale",
                        "保留的旧快照")
                    : CoCoEditorLocalization.Text("Fresh", "最新"),
                stale
                    ? CoCoEditorBadgeKind.Warning
                    : CoCoEditorBadgeKind.Success);

            PopulateKeyValueRows(
                _currentRows,
                _state.BuildCurrentFrameRows());
            _activeStates.Clear();
            List<CoCoDebuggerActiveStateRow> states =
                _state.BuildActiveStateRows();
            if (states.Count == 0)
            {
                var empty = new Label(CoCoEditorLocalization.Text(
                    "No Active State is present in the committed frame.",
                    "当前提交帧中没有 Active State。"));
                empty.AddToClassList("ccflow-muted");
                _activeStates.Add(empty);
                return;
            }

            for (int index = 0; index < states.Count; index++)
            {
                CoCoDebuggerActiveStateRow state = states[index];
                var row = new VisualElement();
                row.AddToClassList("ccflow-debugger-state-row");

                var title = new Label($"{state.Layer}  /  {state.State}");
                title.AddToClassList("ccflow-debugger-state-row__title");
                row.Add(title);

                var details = new Label(string.Format(
                    CultureInfo.InvariantCulture,
                    CoCoEditorLocalization.Text(
                        "ID {0}  ·  Local {1:0.###} s  ·  Progress {2:0.###}  ·  Winner {3}",
                        "ID {0}  ·  局部时间 {1:0.###} s  ·  进度 {2:0.###}  ·  胜出 Transition {3}"),
                    state.StateId,
                    state.LocalSeconds,
                    state.ActionProgress,
                    state.WinningTransition));
                details.AddToClassList("ccflow-debugger-state-row__details");
                details.style.whiteSpace = WhiteSpace.Normal;
                row.Add(details);
                _activeStates.Add(row);
            }
        }

        private void RenderRing()
        {
            CoCoStateGraphHostTemporalDebugSnapshot snapshot = _state.Snapshot;
            _ringStatus.text = snapshot.Capacity == 0
                ? CoCoEditorLocalization.Text(
                    "Temporal history is disabled for this Host.",
                    "该 Host 未启用 Temporal history。")
                : CoCoEditorLocalization.Text(
                    "Depth 0 is current; older Context frames continue " +
                    "clockwise. Select a node for metadata.",
                    "深度 0 是当前帧；更早的 Context 帧按顺时针排列。" +
                    "选择节点可查看元数据。");
            _ring.SetData(
                snapshot,
                _state.SelectedDepth,
                _state.FindPersistedDepth());
            RenderRingSelection();
        }

        private void RenderRingSelection()
        {
            if (_selectedFrameTitle == null || _selectedFrameRows == null)
            {
                return;
            }

            _ring?.SetSelection(
                _state.SelectedDepth,
                _state.FindPersistedDepth());
            _selectedFrameTitle.text = _state.Snapshot == null ||
                                       _state.Snapshot.Count == 0
                ? CoCoEditorLocalization.Text(
                    "No retained Context frame",
                    "没有保留的 Context 帧")
                : string.Format(
                    CultureInfo.InvariantCulture,
                    CoCoEditorLocalization.Text(
                        "Context at depth {0}",
                        "深度 {0} 的 Context"),
                    _state.SelectedDepth);
            PopulateKeyValueRows(
                _selectedFrameRows,
                _state.BuildSelectedFrameRows());
        }

        private void RenderPersistedFrame()
        {
            if (!_state.HasPersistedFrame)
            {
                _persistedRows.Clear();
                return;
            }

            PopulateKeyValueRows(
                _persistedRows,
                _state.BuildPersistedFrameRows());
        }

        private void RenderDiagnostics()
        {
            if (_state.LastRefreshDiagnostic.IsError)
            {
                _diagnostics.Add(CoCoEditorElements.CreateDiagnosticRow(
                    _state.LastRefreshDiagnostic.Message,
                    CoCoEditorBadgeKind.Warning));
            }

            if (!string.IsNullOrEmpty(_state.PersistenceFailure))
            {
                _diagnostics.Add(CoCoEditorElements.CreateDiagnosticRow(
                    _state.PersistenceFailure,
                    CoCoEditorBadgeKind.Warning));
            }

            CoCoStateGraphHostTemporalDebugSnapshot snapshot = _state.Snapshot;
            if (snapshot != null && snapshot.Fault.IsFaulted)
            {
                _diagnostics.Add(CoCoEditorElements.CreateDiagnosticRow(
                    snapshot.Fault.Diagnostic.Message,
                    CoCoEditorBadgeKind.Error));
            }
        }

        private void ShowEmptyState(
            string title,
            string message,
            string firstStep)
        {
            _emptyState.Clear();
            VisualElement content = CoCoEditorElements.CreateEmptyState(
                title,
                message,
                firstStep);
            _emptyState.Add(content);
            _emptyState.style.display = DisplayStyle.Flex;
        }

        private void SetDataCardsVisible(bool visible)
        {
            DisplayStyle display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _currentCard.style.display = display;
            _ringCard.style.display = display;
            _persistedCard.style.display = DisplayStyle.None;
        }

        private void OnHostFieldChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            if (evt.newValue == null)
            {
                ClearTarget();
                return;
            }

            CoCoStateGraphHost host = ResolveHost(evt.newValue);
            if (host == null)
            {
                SyncToolbar();
                return;
            }

            SelectHost(host);
        }

        private void SelectHost(
            CoCoStateGraphHost host,
            bool rememberTarget = true)
        {
            if (host == null)
            {
                if (rememberTarget)
                {
                    ClearTarget();
                }
                else
                {
                    InvalidateLiveHost();
                }

                return;
            }

            if (rememberTarget &&
                CoCoStateGraphDebuggerTargetLocator.TryCapture(
                    host,
                    out CoCoStateGraphDebuggerTargetLocator locator))
            {
                targetLocator = locator;
            }

            if (_host == host)
            {
                SyncToolbar();
                RefreshNow(includePersistence: true);
                return;
            }

            _host = host;
            _nextPersistencePoll = 0d;
            _state.ObserveIdentity(_host);
            SyncToolbar();
            SceneView.RepaintAll();
            RefreshNow(includePersistence: true);
        }

        private bool TryFollowSelection()
        {
            CoCoStateGraphHost host = ResolveFollowTarget(
                Selection.activeObject,
                _host);
            if (host == null)
            {
                return false;
            }

            SelectHost(host);
            return true;
        }

        private void TryRebindTarget()
        {
            if (targetLocator == null ||
                !targetLocator.TryResolve(out CoCoStateGraphHost host))
            {
                return;
            }

            SelectHost(host, rememberTarget: false);
        }

        private void ScheduleTargetRebind()
        {
            if (_rebindScheduled)
            {
                return;
            }

            _rebindScheduled = true;
            EditorApplication.delayCall += RebindAfterModeChange;
        }

        private void RebindAfterModeChange()
        {
            EditorApplication.delayCall -= RebindAfterModeChange;
            _rebindScheduled = false;
            if (this == null)
            {
                return;
            }

            if (!(followSelection && TryFollowSelection()))
            {
                TryRebindTarget();
            }
        }

        private void ClearTarget()
        {
            targetLocator = null;
            InvalidateLiveHost();
        }

        private void InvalidateLiveHost()
        {
            _host = null;
            _nextPersistencePoll = 0d;
            _state.ObserveIdentity(null);
            SyncToolbar();
            if (_emptyState != null)
            {
                Render();
            }

            SceneView.RepaintAll();
        }

        private void SyncToolbar()
        {
            _hostField?.SetValueWithoutNotify(
                _host == null ? null : _host.gameObject);
            _followToggle?.SetValueWithoutNotify(followSelection);
            _sceneMarkerToggle?.SetValueWithoutNotify(showSceneMarker);
        }

        internal static CoCoStateGraphHost ResolveFollowTarget(
            UnityEngine.Object selectedObject,
            CoCoStateGraphHost currentHost)
        {
            return ResolveHost(selectedObject) ?? currentHost;
        }

        internal static CoCoStateGraphHost ResolveHost(
            UnityEngine.Object selectedObject)
        {
            if (selectedObject == null)
            {
                return null;
            }

            if (selectedObject is CoCoStateGraphHost host)
            {
                return host;
            }

            if (selectedObject is GameObject gameObject)
            {
                return gameObject.GetComponentInParent<CoCoStateGraphHost>(true);
            }

            if (selectedObject is Component component)
            {
                return component.GetComponentInParent<CoCoStateGraphHost>(true);
            }

            return null;
        }

        private static void PopulateKeyValueRows(
            VisualElement container,
            IReadOnlyList<CoCoDebuggerKeyValueRow> rows)
        {
            container.Clear();
            for (int index = 0; index < rows.Count; index++)
            {
                CoCoDebuggerKeyValueRow value = rows[index];
                var row = new VisualElement();
                row.AddToClassList("ccflow-host-kv-row");
                var key = new Label(value.Key);
                key.AddToClassList("ccflow-host-kv-row__key");
                row.Add(key);
                var label = new Label(value.Value);
                label.AddToClassList("ccflow-host-kv-row__value");
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);
                container.Add(row);
            }
        }

        private static void SetBadge(
            VisualElement badge,
            string text,
            CoCoEditorBadgeKind kind)
        {
            if (badge == null)
            {
                return;
            }

            CoCoEditorElements.SetBadgeKind(badge, kind);
            Label label = badge.Q<Label>("ccflow-badge-text");
            if (label != null)
            {
                label.text = text;
            }
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            string path = target.name;
            for (Transform parent = target.parent;
                 parent != null;
                 parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        internal static bool TryGetMarkerWorldPosition(
            CoCoStateGraphHost host,
            out Vector3 position)
        {
            if (host == null || host.transform == null)
            {
                position = default;
                return false;
            }

            position = host.transform.position;
            return true;
        }

        private void DrawSceneMarker(SceneView sceneView)
        {
            if (!showSceneMarker ||
                !TryGetMarkerWorldPosition(_host, out Vector3 worldPosition) ||
                sceneView == null ||
                sceneView.camera == null ||
                sceneView.camera.WorldToViewportPoint(worldPosition).z <= 0f ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            float handleSize = HandleUtility.GetHandleSize(worldPosition);
            float radius = handleSize * 0.09f;
            Vector3 markerPosition =
                worldPosition + Vector3.up * handleSize * 0.42f;
            Vector3 stemEnd = markerPosition - Vector3.up * radius * 1.15f;
            Vector3 cameraNormal = sceneView.camera.transform.forward;
            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            try
            {
                // The marker is diagnostic UI, so it stays visible even when
                // the Host pivot is behind scene geometry.
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                Handles.color = SceneMarkerOuterColor;
                Handles.DrawAAPolyLine(4f, worldPosition, stemEnd);
                Handles.DrawSolidDisc(
                    markerPosition,
                    cameraNormal,
                    radius * 1.28f);

                Handles.color = SceneMarkerColor;
                Handles.DrawSolidDisc(markerPosition, cameraNormal, radius);

                Handles.color = SceneMarkerCenterColor;
                Handles.DrawSolidDisc(
                    markerPosition,
                    cameraNormal,
                    radius * 0.28f);
            }
            finally
            {
                Handles.color = previousColor;
                Handles.zTest = previousZTest;
            }
        }
    }

    /// <summary>
    /// Actual circular Temporal-ring selector. Slots are laid out clockwise by
    /// logical history depth; it holds only Editor selection state.
    /// </summary>
    internal sealed class CoCoTemporalRingElement : VisualElement
    {
        private static readonly Color CurrentColor =
            new Color(0.18f, 0.53f, 0.86f, 1f);
        private static readonly Color OccupiedColor =
            new Color(0.42f, 0.46f, 0.52f, 1f);
        private static readonly Color EmptyColor =
            new Color(0.42f, 0.42f, 0.42f, 0.20f);
        private static readonly Color PersistedColor =
            new Color(0.94f, 0.65f, 0.18f, 1f);
        private static readonly Color SelectedColor =
            new Color(0.93f, 0.96f, 1f, 1f);

        private readonly VisualElement _track;
        private readonly VisualElement _nodes;
        private readonly Label _count;
        private readonly Label _caption;
        private readonly List<Button> _slotButtons = new List<Button>();
        private CoCoStateGraphHostTemporalDebugSnapshot _snapshot;
        private int _selectedDepth;
        private int _persistedDepth = -1;

        internal CoCoTemporalRingElement()
        {
            AddToClassList("ccflow-temporal-ring");
            _track = new VisualElement();
            _track.AddToClassList("ccflow-temporal-ring__track");
            Add(_track);

            _nodes = new VisualElement();
            _nodes.AddToClassList("ccflow-temporal-ring__nodes");
            Add(_nodes);

            _count = new Label("0 / 0");
            _count.AddToClassList("ccflow-temporal-ring__count");
            Add(_count);

            _caption = new Label(CoCoEditorLocalization.Text(
                "Temporal Ring",
                "时间环"));
            _caption.AddToClassList("ccflow-temporal-ring__caption");
            Add(_caption);

            RegisterCallback<GeometryChangedEvent>(_ => LayoutRing());
        }

        internal event Action<int> DepthSelected;

        internal void SetData(
            CoCoStateGraphHostTemporalDebugSnapshot snapshot,
            int selectedDepth,
            int persistedDepth)
        {
            _snapshot = snapshot;
            _selectedDepth = selectedDepth;
            _persistedDepth = persistedDepth;
            int capacity = snapshot?.Capacity ?? 0;
            EnsureSlotCount(capacity);
            _count.text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} / {1}",
                snapshot?.Count ?? 0,
                capacity);
            UpdateSlotVisuals();
            LayoutRing();
        }

        internal void SetSelection(int selectedDepth, int persistedDepth)
        {
            _selectedDepth = selectedDepth;
            _persistedDepth = persistedDepth;
            UpdateSlotVisuals();
        }

        private void EnsureSlotCount(int capacity)
        {
            if (_slotButtons.Count == capacity)
            {
                return;
            }

            _nodes.Clear();
            _slotButtons.Clear();
            for (int depth = 0; depth < capacity; depth++)
            {
                int capturedDepth = depth;
                var slot = new Button(() =>
                    DepthSelected?.Invoke(capturedDepth))
                {
                    text = string.Empty,
                    name = $"temporal-slot-{depth}"
                };
                slot.AddToClassList("ccflow-temporal-ring__slot");
                _nodes.Add(slot);
                _slotButtons.Add(slot);
            }
        }

        private void UpdateSlotVisuals()
        {
            int count = _snapshot?.Count ?? 0;
            for (int depth = 0; depth < _slotButtons.Count; depth++)
            {
                Button slot = _slotButtons[depth];
                bool occupied = depth < count;
                bool current = occupied && depth == 0;
                bool selected = occupied && depth == _selectedDepth;
                bool persisted = occupied && depth == _persistedDepth;
                slot.SetEnabled(occupied);
                slot.style.backgroundColor = current
                    ? CurrentColor
                    : occupied
                        ? OccupiedColor
                        : EmptyColor;
                slot.style.borderTopColor =
                    slot.style.borderRightColor =
                    slot.style.borderBottomColor =
                    slot.style.borderLeftColor =
                        persisted
                            ? PersistedColor
                            : selected
                                ? SelectedColor
                                : new Color(0f, 0f, 0f, 0.36f);
                float borderWidth = persisted || selected ? 3f : 1f;
                slot.style.borderTopWidth =
                    slot.style.borderRightWidth =
                    slot.style.borderBottomWidth =
                    slot.style.borderLeftWidth = borderWidth;
                slot.tooltip = occupied
                    ? BuildTooltip(depth, current, persisted)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        CoCoEditorLocalization.Text(
                            "Empty slot {0}",
                            "空槽 {0}"),
                        depth);
            }
        }

        private string BuildTooltip(int depth, bool current, bool persisted)
        {
            CoCoTemporalFrameInfo frame = _snapshot.GetFrame(depth);
            string role = current
                ? CoCoEditorLocalization.Text("Current", "当前")
                : CoCoEditorLocalization.Text("History", "历史");
            if (persisted)
            {
                role += " + " +
                        CoCoEditorLocalization.Text("Persisted", "已持久化");
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                CoCoEditorLocalization.Text(
                    "{0} · Depth {1} · Tick {2} · Revision {3}",
                    "{0} · 深度 {1} · Tick {2} · Revision {3}"),
                role,
                depth,
                frame.TickFrame.Tick.Value,
                frame.Revision.Value);
        }

        private void LayoutRing()
        {
            float width = contentRect.width;
            if (float.IsNaN(width) || width <= 0f)
            {
                return;
            }

            float diameter = Mathf.Clamp(width - 48f, 180f, 330f);
            float left = (width - diameter) * 0.5f;
            float top = 18f;
            _track.style.left = left;
            _track.style.top = top;
            _track.style.width = diameter;
            _track.style.height = diameter;
            _nodes.style.left = left;
            _nodes.style.top = top;
            _nodes.style.width = diameter;
            _nodes.style.height = diameter;

            _count.style.left = left + diameter * 0.25f;
            _count.style.top = top + diameter * 0.39f;
            _count.style.width = diameter * 0.5f;
            _caption.style.left = left + diameter * 0.25f;
            _caption.style.top = top + diameter * 0.53f;
            _caption.style.width = diameter * 0.5f;

            int capacity = _slotButtons.Count;
            if (capacity == 0)
            {
                return;
            }

            float radius = diameter * 0.48f;
            float circumference = 2f * Mathf.PI * radius;
            float slotSize = Mathf.Clamp(
                circumference / capacity * 0.62f,
                6f,
                20f);
            Vector2 center = new Vector2(diameter * 0.5f, diameter * 0.5f);
            for (int depth = 0; depth < capacity; depth++)
            {
                float radians =
                    (-90f + 360f * depth / capacity) * Mathf.Deg2Rad;
                float x = center.x + Mathf.Cos(radians) * radius;
                float y = center.y + Mathf.Sin(radians) * radius;
                Button slot = _slotButtons[depth];
                slot.style.left = x - slotSize * 0.5f;
                slot.style.top = y - slotSize * 0.5f;
                slot.style.width = slotSize;
                slot.style.height = slotSize;
                float corner = slotSize * 0.5f;
                slot.style.borderTopLeftRadius =
                    slot.style.borderTopRightRadius =
                    slot.style.borderBottomLeftRadius =
                    slot.style.borderBottomRightRadius = corner;
            }
        }
    }
}
