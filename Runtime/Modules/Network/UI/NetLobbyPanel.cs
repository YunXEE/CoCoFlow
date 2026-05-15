using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.UI;
using CoCoFlow.Runtime.Modules.Network.Events;

namespace CoCoFlow.Runtime.Modules.Network.UI
{
    public class NetLobbyPanel : UIPanelBase
    {
        [Header("UI References — Inspector 赋值")]
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _disconnectButton;
        [SerializeField] private TMP_InputField _sessionNameInput;
        [SerializeField] private TMP_Text _roomListText;
        [SerializeField] private TMP_Text _statusText;

        private readonly EventAgent _eventAgent = new EventAgent();
        private INetworkSessionController _sessionController;

        protected override void Awake()
        {
            base.Awake();
            BindButtonEvents();
            CoCoServices.WaitFor<INetworkSessionController>(ctrl => _sessionController = ctrl);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _eventAgent.Subscribe<NetSessionListEvent>(OnSessionListUpdated);
            _eventAgent.Subscribe<NetConnectedEvent>(OnConnected);
            _eventAgent.Subscribe<NetDisconnectedEvent>(OnDisconnected);
        }

        protected override void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
            base.OnDisable();
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
            string sessionName = _sessionNameInput != null ? _sessionNameInput.text : "DefaultSession";
            _sessionController.StartHost(sessionName, default).Forget();
            UpdateStatus("正在创建主机...");
        }

        private void OnJoinClicked()
        {
            if (_sessionController == null)
            {
                UpdateStatus("网络会话服务未就绪");
                return;
            }
            string sessionName = _sessionNameInput != null ? _sessionNameInput.text : "DefaultSession";
            _sessionController.StartClient(sessionName).Forget();
            UpdateStatus("正在加入会话...");
        }

        private void OnDisconnectClicked()
        {
            _sessionController?.Disconnect();
            UpdateStatus("已断开连接");
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
        }

        private void OnDisconnected(ref NetDisconnectedEvent evt)
        {
            UpdateStatus($"已断开: {evt.Reason}");
        }

        private void UpdateStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }
        #endregion
    }
}
