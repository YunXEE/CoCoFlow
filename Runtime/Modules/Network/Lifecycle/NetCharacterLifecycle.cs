using Fusion;
using UnityEngine;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Modules.Network.Events;

namespace CoCoFlow.Runtime.Modules.Network.Lifecycle
{
    /// <summary>
    /// 网络同步角色生命系统：通过 Fusion [Networked] 属性与 RPC 桥接至 CoCoEventBus。
    /// </summary>
    public class NetCharacterLifecycle : NetworkBehaviour
    {
        [Header("Defaults")]
        [SerializeField] private float _defaultMaxHealth = 100f;

        [Networked, OnChangedRender(nameof(OnHealthRenderChanged))]
        public float CurrentHealth { get; set; }

        [Networked, OnChangedRender(nameof(OnDeathRenderChanged))]
        public NetworkBool IsDead { get; set; }

        [Networked]
        public float MaxHealth { get; set; } = 100f;

        private CharacterLifeCycle _lifecycle;

        #region Unity & Fusion Lifecycle

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                CurrentHealth = _defaultMaxHealth;
                MaxHealth = _defaultMaxHealth;
                IsDead = false;
            }

            _lifecycle = GetComponent<CharacterLifeCycle>();
        }

        #endregion

        #region RPCs

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestDamage(float amount, PlayerRef attacker)
        {
            if (!HasStateAuthority) return;

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);

            if (_lifecycle != null)
                _lifecycle.TakeDamage(amount);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestRevive(float healthPercentage = 0.5f)
        {
            if (!HasStateAuthority || !IsDead) return;

            IsDead = false;
            CurrentHealth = MaxHealth * Mathf.Clamp01(healthPercentage);

            if (_lifecycle != null)
                _lifecycle.Revive(healthPercentage);
        }

        #endregion

        #region Networked Callbacks

        private void OnHealthRenderChanged()
        {
            var evt = new NetHealthChangedEvent
            {
                Player = Object.InputAuthority,
                CurrentHealth = CurrentHealth,
                MaxHealth = MaxHealth
            };
            CoCoEventBus.Publish(ref evt);

            if (CurrentHealth <= 0f && !IsDead)
                IsDead = true;
        }

        private void OnDeathRenderChanged()
        {
            if (IsDead)
            {
                var evt = new NetDeathEvent { Player = Object.InputAuthority };
                CoCoEventBus.Publish(ref evt);
            }
            else
            {
                var evt = new NetReviveEvent { Player = Object.InputAuthority };
                CoCoEventBus.Publish(ref evt);
            }
        }

        #endregion
    }
}
