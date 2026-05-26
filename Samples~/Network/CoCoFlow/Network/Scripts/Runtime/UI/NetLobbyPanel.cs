using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.UI;
using CoCoFlow.Runtime.Addon.Network.Events;

namespace CoCoFlow.Runtime.Addon.Network.UI
{
    public class NetLobbyPanel : UIPanelBase
    {
        [Header("UI References — Inspector 赋值")]
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _startGameButton;
        [SerializeField] private Button _disconnectButton;
        [SerializeField] private TMP_InputField _sessionNameInput;
        [SerializeField] private TMP_Text _roomListText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _playerCountText;
        [SerializeField] private int _gameSceneBuildIndex = -1;

        private readonly EventAgent _eventAgent = new EventAgent();
        private INetworkSessionController _sessionController;
        private INetworkRunnerProvider _runnerProvider;
        private int _playerCount;

        protected override void Awake()
        {
            base.Awake();
            BindButtonEvents();
            CoCoServices.WaitFor<INetworkSessionController>(ctrl =>
            {
                _sessionController = ctrl;
                RefreshControls();
            });
            CoCoServices.WaitFor<INetworkRunnerProvider>(provider =>
            {
                _runnerProvider = provider;
                RefreshControls();
            });
            UpdateStatus("未连接");
            UpdatePlayerCount();
        }

        protected void OnEnable()
        {
            _eventAgent.Subscribe<NetSessionListEvent>(OnSessionListUpdated);
            _eventAgent.Subscribe<NetSessionStateChangedEvent>(OnSessionStateChanged);
            _eventAgent.Subscribe<NetSessionStartFailedEvent>(OnSessionStartFailed);
            _eventAgent.Subscribe<NetConnectedEvent>(OnConnected);
            _eventAgent.Subscribe<NetDisconnectedEvent>(OnDisconnected);
            _eventAgent.Subscribe<NetShutdownEvent>(OnShutdown);
            _eventAgent.Subscribe<NetPlayerJoinedEvent>(OnPlayerJoined);
            _eventAgent.Subscribe<NetPlayerLeftEvent>(OnPlayerLeft);
            _eventAgent.Subscribe<NetPlayerObjectSpawnedEvent>(OnPlayerObjectSpawned);
            _eventAgent.Subscribe<NetMatchStartedEvent>(OnMatchStarted);
            RefreshControls();
        }

        protected void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
        }

        protected override void OnDestroy()
        {
            _eventAgent.UnsubscribeAll();
            base.OnDestroy();
        }

        #region Internal Logic
        private void BindButtonEvents()
        {
            if (_hostButton != null)
                _hostButton.onClick.AddListener(OnHostClicked);
            if (_joinButton != null)
                _joinButton.onClick.AddListener(OnJoinClicked);
            if (_startGameButton != null)
                _startGameButton.onClick.AddListener(OnStartGameClicked);
            if (_disconnectButton != null)
                _disconnectButton.onClick.AddListener(OnDisconnectClicked);
        }

        private void OnHostClicked()
        {
            if (_sessionController == null)
            {
                UpdateStatus("网络会话服务未就绪");
                return;
            }
            var sessionName = GetSessionName();
            _sessionController.StartHost(sessionName, default).Forget();
            UpdateStatus("正在创建主机...");
            RefreshControls();
        }

        private void OnJoinClicked()
        {
            if (_sessionController == null)
            {
                UpdateStatus("网络会话服务未就绪");
                return;
            }
            var sessionName = GetSessionName();
            _sessionController.StartClient(sessionName).Forget();
            UpdateStatus("正在加入会话...");
            RefreshControls();
        }

        private void OnStartGameClicked()
        {
            if (_sessionController == null)
            {
                UpdateStatus("网络会话服务未就绪");
                return;
            }

            if (_runnerProvider == null || !_runnerProvider.CanSpawnPlayerObjects)
            {
                UpdateStatus("只有 Host 可以开始游戏");
                return;
            }

            var scene = _gameSceneBuildIndex >= 0 ? Fusion.SceneRef.FromIndex(_gameSceneBuildIndex) : default;
            if (!scene.IsValid)
            {
                UpdateStatus("请先配置 Game Scene Build Index");
                return;
            }

            _sessionController.StartGame(scene).Forget();
            UpdateStatus("正在切换游戏场景...");
            RefreshControls();
        }

        private void OnDisconnectClicked()
        {
            _sessionController?.Disconnect();
            UpdateStatus("已断开连接");
            _playerCount = 0;
            UpdatePlayerCount();
            RefreshControls();
        }

        private void OnSessionListUpdated(ref NetSessionListEvent evt)
        {
            if (_roomListText == null || evt.Sessions == null)
                return;

            _roomListText.text = evt.Sessions.Count > 0
                ? $"可用房间: {evt.Sessions.Count}"
                : "暂无可用房间";
            // TODO: 将 SessionInfo 列表填充到可滚动的房间列表 UI 中
        }

        private void OnConnected(ref NetConnectedEvent evt)
        {
            UpdateStatus("已连接");
            RefreshControls();
        }

        private void OnDisconnected(ref NetDisconnectedEvent evt)
        {
            UpdateStatus($"已断开: {evt.Reason}");
            _playerCount = 0;
            UpdatePlayerCount();
            RefreshControls();
        }

        private void OnShutdown(ref NetShutdownEvent evt)
        {
            UpdateStatus($"网络已关闭: {evt.Reason}");
            _playerCount = 0;
            UpdatePlayerCount();
            RefreshControls();
        }

        private void OnSessionStateChanged(ref NetSessionStateChangedEvent evt)
        {
            _playerCount = evt.PlayerCount;
            UpdatePlayerCount();

            if (!string.IsNullOrEmpty(evt.Message))
                UpdateStatus(evt.Message);

            RefreshControls();
        }

        private void OnSessionStartFailed(ref NetSessionStartFailedEvent evt)
        {
            var detail = string.IsNullOrEmpty(evt.ErrorMessage) ? evt.Reason.ToString() : evt.ErrorMessage;
            UpdateStatus($"启动失败: {detail}");
            RefreshControls();
        }

        private void OnPlayerJoined(ref NetPlayerJoinedEvent evt)
        {
            _playerCount = CountActivePlayers();
            UpdatePlayerCount();
            RefreshControls();
        }

        private void OnPlayerLeft(ref NetPlayerLeftEvent evt)
        {
            _playerCount = CountActivePlayers();
            UpdatePlayerCount();
            RefreshControls();
        }

        private void OnPlayerObjectSpawned(ref NetPlayerObjectSpawnedEvent evt)
        {
            UpdateStatus($"玩家已生成: {evt.Player}");
        }

        private void OnMatchStarted(ref NetMatchStartedEvent evt)
        {
            UpdateStatus("游戏已开始");
            RefreshControls();
        }

        private void UpdateStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }

        private void UpdatePlayerCount()
        {
            if (_playerCountText != null)
                _playerCountText.text = $"Players: {_playerCount}";
        }

        private string GetSessionName()
        {
            if (_sessionNameInput == null || string.IsNullOrWhiteSpace(_sessionNameInput.text))
                return "DefaultSession";

            return _sessionNameInput.text.Trim();
        }

        private int CountActivePlayers()
        {
            var runner = _runnerProvider?.Runner;
            if (runner == null || !runner.IsRunning) return _playerCount;

            var count = 0;
            foreach (var _ in runner.ActivePlayers)
                count++;
            return count;
        }

        private void RefreshControls()
        {
            var isConnected = _runnerProvider != null && _runnerProvider.IsConnected;
            var canStartGame = _runnerProvider != null && _runnerProvider.CanSpawnPlayerObjects;

            if (_hostButton != null)
                _hostButton.interactable = !isConnected && _sessionController != null;
            if (_joinButton != null)
                _joinButton.interactable = !isConnected && _sessionController != null;
            if (_startGameButton != null)
                _startGameButton.interactable = isConnected && canStartGame && _sessionController != null;
            if (_disconnectButton != null)
                _disconnectButton.interactable = isConnected && _sessionController != null;
        }
        #endregion
    }
}
