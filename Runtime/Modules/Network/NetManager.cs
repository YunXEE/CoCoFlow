using System;
using System.Collections.Generic;
using Fusion;
using Cysharp.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Network.Input;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Network
{
    /// <summary>
    /// 网络模块总入口：负责 NetworkRunner 的创建/配置/销毁，
    /// 将所有 Fusion 回调桥接至 CoCoEventBus 事件总线。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class NetManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private NetworkRunner _runnerPrefab;

        private NetworkRunner _runner;
        private bool _isConnected;
        private PlayerRef _localPlayer;

        private NetworkInputBridge _inputBridge;

        #region Public API

        public NetworkRunner Runner => _runner;

        public bool IsConnected => _isConnected;

        public PlayerRef LocalPlayer => _localPlayer;

        public async UniTask StartHost(string sessionName, SceneRef scene)
        {
            if (_runner == null || _runner.IsRunning) return;

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = sessionName,
                Scene = scene,
                SceneManager = _runner.GetComponent<INetworkSceneManager>()
                    ?? _runner.gameObject.AddComponent<NetworkSceneManagerDefault>()
            });

            if (result.Ok)
                _isConnected = true;
            else
                CoCoLog.Error($"[NetManager] StartHost 失败: {result.ShutdownReason}");
        }

        public async UniTask StartClient(string sessionName)
        {
            if (_runner == null || _runner.IsRunning) return;

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = sessionName,
                SceneManager = _runner.GetComponent<INetworkSceneManager>()
                    ?? _runner.gameObject.AddComponent<NetworkSceneManagerDefault>()
            });

            if (result.Ok)
                _isConnected = true;
            else
                CoCoLog.Error($"[NetManager] StartClient 失败: {result.ShutdownReason}");
        }

        public void Disconnect()
        {
            _isConnected = false;
            _runner?.Shutdown();
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            CoCoServices.Register<INetworkRunnerProvider>(this);
            CoCoServices.Register<INetworkSessionController>(this);

            InitRunner();
            _inputBridge = GetComponent<NetworkInputBridge>();
        }

        private void OnDestroy()
        {
            CoCoServices.Unregister<INetworkRunnerProvider>(this);
            CoCoServices.Unregister<INetworkSessionController>(this);
            _runner?.Shutdown();
        }

        #endregion

        #region Internal Logic

        private void InitRunner()
        {
            if (_runnerPrefab != null)
            {
                var instance = Instantiate(_runnerPrefab);
                DontDestroyOnLoad(instance);
                _runner = instance.GetComponent<NetworkRunner>();
                if (_runner == null)
                    _runner = instance.AddComponent<NetworkRunner>();
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

        #endregion

        #region INetworkRunnerCallbacks — 桥接至 CoCoEventBus

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner.LocalPlayer == player)
                _localPlayer = player;

            var evt = new NetPlayerJoinedEvent { Player = player };
            CoCoEventBus.Publish(ref evt);
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            var evt = new NetPlayerLeftEvent { Player = player };
            CoCoEventBus.Publish(ref evt);
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            _isConnected = true;
            var evt = new NetConnectedEvent { Runner = runner };
            CoCoEventBus.Publish(ref evt);
        }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            _isConnected = false;
            var evt = new NetDisconnectedEvent { Reason = reason };
            CoCoEventBus.Publish(ref evt);
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            _isConnected = false;
            var evt = new NetShutdownEvent { Reason = shutdownReason };
            CoCoEventBus.Publish(ref evt);
        }

        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
            var evt = new NetSceneReadyEvent { Runner = runner };
            CoCoEventBus.Publish(ref evt);
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
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
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
