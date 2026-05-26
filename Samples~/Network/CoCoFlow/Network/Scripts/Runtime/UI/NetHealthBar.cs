using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.UI;
using CoCoFlow.Runtime.Addon.Network.Events;

namespace CoCoFlow.Runtime.Addon.Network.UI
{
    public class NetHealthBar : UIPanelBase
    {
        [Header("UI References — Inspector 赋值")]
        [SerializeField] private Slider _healthSlider;
        [SerializeField] private TMP_Text _healthText;
        [SerializeField] private TMP_Text _playerNameText;

        private readonly EventAgent _eventAgent = new EventAgent();
        private bool _isDead;

        protected override void Awake()
        {
            base.Awake();
            if (_healthSlider != null)
            {
                _healthSlider.minValue = 0f;
                _healthSlider.wholeNumbers = false;
            }
        }

        protected void OnEnable()
        {
            _eventAgent.Subscribe<NetHealthChangedEvent>(OnHealthChanged);
            _eventAgent.Subscribe<NetDeathEvent>(OnDeath);
            _eventAgent.Subscribe<NetReviveEvent>(OnRevive);
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

        #region Public API
        /// <summary>
        /// 手动设置血量显示（用于初始化/本地预览，不依赖网络事件）
        /// </summary>
        public void SetHealth(float current, float max)
        {
            if (_healthSlider != null)
            {
                _healthSlider.maxValue = max;
                _healthSlider.value = current;
            }
            if (_healthText != null)
                _healthText.text = $"{current:F0}/{max:F0}";
        }
        #endregion

        #region Internal Logic
        private void OnHealthChanged(ref NetHealthChangedEvent evt)
        {
            _isDead = false;
            if (_healthSlider != null)
            {
                _healthSlider.maxValue = evt.MaxHealth;
                _healthSlider.value = evt.CurrentHealth;
            }
            if (_healthText != null)
                _healthText.text = $"{evt.CurrentHealth:F0}/{evt.MaxHealth:F0}";
            if (_playerNameText != null)
                _playerNameText.text = evt.Player.ToString();
        }

        private void OnDeath(ref NetDeathEvent evt)
        {
            _isDead = true;
            if (_healthSlider != null)
                _healthSlider.value = 0f;
            if (_healthText != null)
                _healthText.text = "阵亡";
        }

        private void OnRevive(ref NetReviveEvent evt)
        {
            _isDead = false;
            if (_healthText != null)
                _healthText.text = "存活";
        }
        #endregion
    }
}
