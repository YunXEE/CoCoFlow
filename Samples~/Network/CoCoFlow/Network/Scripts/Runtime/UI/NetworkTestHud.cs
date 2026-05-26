using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Addon.Network.Events;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.UI
{
    /// <summary>
    /// 网络作业测试用最小 Lobby HUD。
    /// 使用 OnGUI 避免测试骨架依赖正式 UI 预制体；正式项目可替换为 NetLobbyPanel。
    /// </summary>
    public class NetworkTestHud : MonoBehaviour
    {
        [SerializeField] private string _sessionName = "CoCoFlowRoom";
        [SerializeField] private int _gameSceneBuildIndex = -1;

        private readonly EventAgent _eventAgent = new EventAgent();
        private INetworkSessionController _sessionController;
        private INetworkRunnerProvider _runnerProvider;
        private string _status = "未连接";
        private int _playerCount;

        private void Awake()
        {
            CoCoServices.WaitFor<INetworkSessionController>(svc => _sessionController = svc);
            CoCoServices.WaitFor<INetworkRunnerProvider>(svc => _runnerProvider = svc);
        }

        private void OnEnable()
        {
            _eventAgent.Subscribe<NetSessionStateChangedEvent>(OnSessionStateChanged);
            _eventAgent.Subscribe<NetSessionStartFailedEvent>(OnSessionStartFailed);
            _eventAgent.Subscribe<NetPlayerObjectSpawnedEvent>(OnPlayerObjectSpawned);
            _eventAgent.Subscribe<NetShutdownEvent>(OnShutdown);
        }

        private void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
        }

        private void OnDestroy()
        {
            _eventAgent.UnsubscribeAll();
        }

        private void OnGUI()
        {
            const float width = 320f;
            GUILayout.BeginArea(new Rect(16f, 16f, width, 260f), GUI.skin.box);
            GUILayout.Label("CoCoFlow Fusion Test");
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label($"Players: {_playerCount}");

            GUILayout.Space(6f);
            GUILayout.Label("Room");
            _sessionName = GUILayout.TextField(_sessionName);

            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = CanStartSession();
                if (GUILayout.Button("Create Room"))
                    StartHost().Forget();

                if (GUILayout.Button("Join Room"))
                    StartClient().Forget();
                GUI.enabled = true;
            }

            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = _runnerProvider != null && _runnerProvider.CanSpawnPlayerObjects;
                if (GUILayout.Button("Start Game"))
                    StartGame().Forget();
                GUI.enabled = true;

                GUI.enabled = _runnerProvider != null && _runnerProvider.IsConnected;
                if (GUILayout.Button("Disconnect"))
                    _sessionController?.Disconnect();
                GUI.enabled = true;
            }

            GUILayout.Space(6f);
            GUILayout.Label("Host + 1-2 Client 使用相同 Room 名称。");
            GUILayout.EndArea();
        }

        #region Internal Logic

        private async UniTask StartHost()
        {
            if (_sessionController == null) return;
            _status = "正在创建房间...";
            await _sessionController.StartHost(GetSessionName(), default);
        }

        private async UniTask StartClient()
        {
            if (_sessionController == null) return;
            _status = "正在加入房间...";
            await _sessionController.StartClient(GetSessionName());
        }

        private async UniTask StartGame()
        {
            if (_sessionController == null) return;

            var scene = _gameSceneBuildIndex >= 0 ? SceneRef.FromIndex(_gameSceneBuildIndex) : default;
            if (!scene.IsValid)
            {
                _status = "请先配置 Game Scene Build Index";
                return;
            }

            _status = "正在切换游戏场景...";
            await _sessionController.StartGame(scene);
        }

        private bool CanStartSession()
        {
            return _sessionController != null &&
                   (_runnerProvider == null || !_runnerProvider.IsConnected);
        }

        private string GetSessionName()
        {
            return string.IsNullOrWhiteSpace(_sessionName) ? "CoCoFlowRoom" : _sessionName.Trim();
        }

        private void OnSessionStateChanged(ref NetSessionStateChangedEvent evt)
        {
            _status = string.IsNullOrEmpty(evt.Message) ? evt.State.ToString() : evt.Message;
            _playerCount = evt.PlayerCount;
        }

        private void OnSessionStartFailed(ref NetSessionStartFailedEvent evt)
        {
            _status = string.IsNullOrEmpty(evt.ErrorMessage)
                ? $"启动失败: {evt.Reason}"
                : $"启动失败: {evt.ErrorMessage}";
        }

        private void OnPlayerObjectSpawned(ref NetPlayerObjectSpawnedEvent evt)
        {
            _status = $"玩家已生成: {evt.Player}";
        }

        private void OnShutdown(ref NetShutdownEvent evt)
        {
            _status = $"网络已关闭: {evt.Reason}";
            _playerCount = 0;
        }

        #endregion
    }
}
