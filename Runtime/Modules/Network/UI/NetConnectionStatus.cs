using UnityEngine;
using TMPro;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.UI;
using CoCoFlow.Runtime.Modules.Network.Events;

namespace CoCoFlow.Runtime.Modules.Network.UI
{
    public class NetConnectionStatus : UIPanelBase
    {
        [Header("UI References — Inspector 赋值")]
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private GameObject _connectedIndicator;
        [SerializeField] private GameObject _disconnectedIndicator;

        private readonly EventAgent _eventAgent = new EventAgent();

        protected override void Awake()
        {
            base.Awake();
            SetConnectionVisuals(false);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _eventAgent.Subscribe<NetConnectedEvent>(OnConnected);
            _eventAgent.Subscribe<NetDisconnectedEvent>(OnDisconnected);
            _eventAgent.Subscribe<NetShutdownEvent>(OnShutdown);
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
        private void OnConnected(ref NetConnectedEvent evt)
        {
            SetConnectionVisuals(true);
            if (_statusText != null)
                _statusText.text = "已连接";
        }

        private void OnDisconnected(ref NetDisconnectedEvent evt)
        {
            SetConnectionVisuals(false);
            if (_statusText != null)
                _statusText.text = $"已断开: {evt.Reason}";
        }

        private void OnShutdown(ref NetShutdownEvent evt)
        {
            SetConnectionVisuals(false);
            if (_statusText != null)
                _statusText.text = $"网络已关闭: {evt.Reason}";
        }

        private void SetConnectionVisuals(bool isConnected)
        {
            if (_connectedIndicator != null)
                _connectedIndicator.SetActive(isConnected);
            if (_disconnectedIndicator != null)
                _disconnectedIndicator.SetActive(!isConnected);
        }
        #endregion
    }
}
