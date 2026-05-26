using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using Cysharp.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Addon.Network.Input;
using CoCoFlow.Runtime.Addon.Network.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Addon.Network
{
    /// <summary>
    /// 网络模块总入口：负责 NetworkRunner 的创建/配置/销毁，
    /// 将所有 Fusion 回调桥接至 CoCoEventBus 事件总线。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class NetManager : MonoBehaviour, INetworkRunnerCallbacks, INetworkRunnerProvider, INetworkSessionController
    {
        [SerializeField] private NetworkRunner _runnerPrefab;
        [SerializeField] private string _defaultSessionName = "DefaultSession";
        [SerializeField] private int _defaultPlayerCount = 8;
        [SerializeField] private bool _dontDestroyOnLoad = true;

        private static NetManager _instance;
        private NetworkRunner _runner;
        private bool _isConnected;
        private bool _isQuitting;
        private PlayerRef _localPlayer;
        private GameMode _currentGameMode = GameMode.Single;
        private NetSessionMode _currentSessionMode = NetSessionMode.Host;
        private NetSessionState _currentState = NetSessionState.Idle;
        private string _currentSessionName;

        private NetworkInputBridge _inputBridge;

        #region Public API

        public NetworkRunner Runner => _runner;

        public bool IsConnected => _isConnected;

        public PlayerRef LocalPlayer => _localPlayer;

        public GameMode CurrentGameMode => _runner != null && _runner.IsRunning ? _runner.GameMode : _currentGameMode;

        public string CurrentSessionName => _currentSessionName;

        public bool CanSpawnPlayerObjects =>
            _runner != null &&
            _runner.IsRunning &&
            (_runner.GameMode == GameMode.Shared ? _runner.IsSharedModeMasterClient : _runner.IsServer);

        public async UniTask StartSession(NetSessionStartOptions options)
        {
            if (_runner == null)
                InitRunner();

            if (_runner == null)
            {
                PublishStartFailed(options, ShutdownReason.Error, "NetworkRunner 未初始化");
                return;
            }

            if (_runner.IsRunning)
            {
                CoCoLog.Warning("[NetManager] NetworkRunner 已在运行，忽略重复启动请求。");
                return;
            }

            var normalizedOptions = NormalizeOptions(options);
            _currentSessionMode = normalizedOptions.Mode;
            _currentGameMode = ToFusionGameMode(normalizedOptions.Mode);
            _currentSessionName = normalizedOptions.SessionName;

            PublishState(NetSessionState.Starting, "正在启动网络会话");

            var args = new StartGameArgs
            {
                GameMode = _currentGameMode,
                SessionName = normalizedOptions.SessionName,
                PlayerCount = normalizedOptions.PlayerCount,
                IsOpen = normalizedOptions.IsOpen,
                IsVisible = normalizedOptions.IsVisible,
                SceneManager = GetOrCreateSceneManager()
            };

            if (normalizedOptions.Scene.IsValid)
            {
                var sceneInfo = new NetworkSceneInfo();
                sceneInfo.AddSceneRef(normalizedOptions.Scene, LoadSceneMode.Single);
                args.Scene = sceneInfo;
            }

            StartGameResult result;
            try
            {
                result = await _runner.StartGame(args);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                PublishStartFailed(normalizedOptions, ShutdownReason.Error, ex.Message);
                CoCoLog.Error($"[NetManager] StartSession 异常: {ex}");
                ReleaseRunner(true);
                return;
            }

            if (result.Ok)
            {
                _isConnected = true;
                PublishState(NetSessionState.Connected, "网络会话已启动");
            }
            else
            {
                _isConnected = false;
                PublishStartFailed(normalizedOptions, result.ShutdownReason, result.ErrorMessage);
                CoCoLog.Error($"[NetManager] StartSession 失败: {result.ShutdownReason} {result.ErrorMessage}");
                ReleaseRunner(true);
            }
        }

        public async UniTask StartHost(string sessionName, SceneRef scene)
        {
            await StartSession(NetSessionStartOptions.Host(sessionName, scene));
        }

        public async UniTask StartClient(string sessionName)
        {
            await StartSession(NetSessionStartOptions.Client(sessionName));
        }

        public async UniTask StartShared(string sessionName, SceneRef scene)
        {
            await StartSession(NetSessionStartOptions.Shared(sessionName, scene));
        }

        public UniTask StartGame(SceneRef gameScene)
        {
            if (_runner == null || !_runner.IsRunning)
            {
                PublishStartFailed(
                    new NetSessionStartOptions
                    {
                        Mode = _currentSessionMode,
                        SessionName = _currentSessionName
                    },
                    ShutdownReason.Error,
                    "NetworkRunner 未运行，无法开始游戏");
                return UniTask.CompletedTask;
            }

            if (!CanSpawnPlayerObjects)
            {
                CoCoLog.Warning("[NetManager] 只有 Host 或 Shared Master Client 可以发起 StartGame。");
                return UniTask.CompletedTask;
            }

            if (!gameScene.IsValid)
            {
                CoCoLog.Warning("[NetManager] 未配置有效游戏场景，StartGame 已取消。");
                PublishState(_currentState, "未配置有效游戏场景，无法开始游戏");
                return UniTask.CompletedTask;
            }

            _runner.LoadScene(gameScene, LoadSceneMode.Single, LocalPhysicsMode.None, true);

            var matchStarted = new NetMatchStartedEvent { Runner = _runner, Scene = gameScene };
            CoCoEventBus.Publish(ref matchStarted);

            PublishState(NetSessionState.InGame, "正在切换游戏场景");
            return UniTask.CompletedTask;
        }

        public void Disconnect()
        {
            _isConnected = false;
            if (_runner != null && _runner.IsRunning)
                _runner.Shutdown();
            else
                PublishState(NetSessionState.Shutdown, "网络已关闭");
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                CoCoLog.Warning("[NetManager] 场景中存在重复 NetManager，已销毁后创建的实例。");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            CoCoServices.Register<INetworkRunnerProvider>(this);
            CoCoServices.Register<INetworkSessionController>(this);

            InitRunner();
            _inputBridge = GetComponent<NetworkInputBridge>();
        }

        private void OnDestroy()
        {
            if (_instance != this)
                return;

            _isQuitting = true;
            CoCoServices.Unregister<INetworkRunnerProvider>(this);
            CoCoServices.Unregister<INetworkSessionController>(this);
            if (_runner != null && _runner.IsRunning)
                _runner.Shutdown();
            ReleaseRunner(true);

            _instance = null;
        }

        #endregion

        #region Internal Logic

        private void InitRunner()
        {
            if (_runner != null) return;

            if (_runnerPrefab != null)
            {
                var instance = Instantiate(_runnerPrefab);
                DontDestroyOnLoad(instance);
                _runner = instance.GetComponent<NetworkRunner>();
                if (_runner == null)
                    _runner = instance.gameObject.AddComponent<NetworkRunner>();
            }
            else
            {
                var obj = new GameObject("NetworkRunner");
                DontDestroyOnLoad(obj);
                _runner = obj.AddComponent<NetworkRunner>();
            }

            _runner.AddCallbacks(this);
            _runner.ProvideInput = true;
        }

        private void ReleaseRunner(bool destroyRunnerObject)
        {
            if (_runner == null) return;

            _runner.RemoveCallbacks(this);
            var runnerObject = _runner.gameObject;
            _runner = null;

            if (destroyRunnerObject && runnerObject != null && runnerObject != gameObject)
                Destroy(runnerObject);
        }

        private INetworkSceneManager GetOrCreateSceneManager()
        {
            return _runner.GetComponent<INetworkSceneManager>()
                ?? _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        private NetSessionStartOptions NormalizeOptions(NetSessionStartOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.SessionName))
                options.SessionName = string.IsNullOrWhiteSpace(_defaultSessionName) ? "DefaultSession" : _defaultSessionName;
            else
                options.SessionName = options.SessionName.Trim();

            if (options.PlayerCount <= 0)
                options.PlayerCount = Mathf.Max(1, _defaultPlayerCount);

            // 默认面向作业验收：房间可见且可加入。
            if (!options.IsOpen && !options.IsVisible)
            {
                options.IsOpen = true;
                options.IsVisible = true;
            }

            return options;
        }

        private static GameMode ToFusionGameMode(NetSessionMode mode)
        {
            return mode switch
            {
                NetSessionMode.Client => GameMode.Client,
                NetSessionMode.Shared => GameMode.Shared,
                _ => GameMode.Host
            };
        }

        private void PublishState(NetSessionState state, string message)
        {
            _currentState = state;
            var evt = new NetSessionStateChangedEvent
            {
                State = state,
                Mode = _currentSessionMode,
                SessionName = _currentSessionName,
                PlayerCount = CountActivePlayers(),
                Message = message,
                Runner = _runner
            };
            CoCoEventBus.Publish(ref evt);
        }

        private void PublishStartFailed(NetSessionStartOptions options, ShutdownReason reason, string errorMessage)
        {
            _currentState = NetSessionState.Failed;
            _currentSessionMode = options.Mode;
            _currentSessionName = options.SessionName;

            var failed = new NetSessionStartFailedEvent
            {
                Mode = options.Mode,
                SessionName = options.SessionName,
                Reason = reason,
                ErrorMessage = errorMessage
            };
            CoCoEventBus.Publish(ref failed);

            PublishState(NetSessionState.Failed, string.IsNullOrEmpty(errorMessage) ? reason.ToString() : errorMessage);
        }

        private int CountActivePlayers()
        {
            if (_runner == null || !_runner.IsRunning) return 0;

            var count = 0;
            foreach (var _ in _runner.ActivePlayers)
                count++;
            return count;
        }

        #endregion

        #region INetworkRunnerCallbacks — 桥接至 CoCoEventBus

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner.LocalPlayer == player)
                _localPlayer = player;

            var evt = new NetPlayerJoinedEvent { Player = player };
            CoCoEventBus.Publish(ref evt);
            PublishState(_currentState == NetSessionState.Idle ? NetSessionState.Connected : _currentState, "玩家已加入");
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            var evt = new NetPlayerLeftEvent { Player = player };
            CoCoEventBus.Publish(ref evt);
            PublishState(_currentState, "玩家已离开");
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            _isConnected = true;
            var evt = new NetConnectedEvent { Runner = runner };
            CoCoEventBus.Publish(ref evt);
            PublishState(NetSessionState.Connected, "已连接服务器");
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            _isConnected = false;
            var evt = new NetDisconnectedEvent { Reason = reason };
            CoCoEventBus.Publish(ref evt);
            PublishState(NetSessionState.Shutdown, $"已断开: {reason}");
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            _isConnected = false;
            _localPlayer = default;
            var evt = new NetShutdownEvent { Reason = shutdownReason };
            CoCoEventBus.Publish(ref evt);
            PublishState(NetSessionState.Shutdown, $"网络已关闭: {shutdownReason}");

            if (!_isQuitting && ReferenceEquals(_runner, runner))
                ReleaseRunner(true);
        }

        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
            var evt = new NetSceneReadyEvent { Runner = runner };
            CoCoEventBus.Publish(ref evt);
            if (_currentState == NetSessionState.InGame)
                PublishState(NetSessionState.InGame, "游戏场景已就绪");
        }

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            // 懒查找：Awake 时可能 NetworkInputBridge 尚未挂载
            if (_inputBridge == null)
                _inputBridge = GetComponent<NetworkInputBridge>();

            if (_inputBridge != null)
            {
                var playerInput = _inputBridge.ConsumeAndReset();
                input.Set(playerInput);
            }
        }

        // 以下 Fusion 回调暂不处理，保留桩实现以确保编译通过
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            var evt = new NetSessionListEvent { Sessions = sessionList };
            CoCoEventBus.Publish(ref evt);
        }
        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

        #endregion
    }
}
