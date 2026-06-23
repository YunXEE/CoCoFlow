using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    public class CharacterLifeCycle : MonoBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;

        [Header("Context")]
        [Tooltip("CharacterContext Provider。若为空，会在当前 GameObject 上自动查找匹配的 Provider。")]
        [SerializeField] private MonoBehaviour contextProvider;

        private CharacterContext _context;

        #region Public API

        public CharacterContext Context => ResolveContext();
        public float CurrentHealth => Context?.Resources.CurrentHealth ?? 0f;
        public bool IsDead => Context == null || Context.Resources.IsDead;

        public event Action<float, float> OnHealthChanged;
        public event Action<float> OnTakeDamage;
        public event Action OnDeath;
        public event Action OnRevive;

        public void TakeDamage(float damageAmount)
        {
            var targetContext = Context;
            if (targetContext == null || targetContext.Resources.IsDead || damageAmount <= 0) return;

            var died = targetContext.Resources.ApplyDamage(damageAmount);

            OnTakeDamage?.Invoke(damageAmount);
            OnHealthChanged?.Invoke(targetContext.Resources.CurrentHealth, targetContext.Resources.MaxHealth);

            if (died)
            {
                Die(targetContext);
            }
        }

        public void Heal(float healAmount)
        {
            var targetContext = Context;
            if (targetContext == null || targetContext.Resources.IsDead || healAmount <= 0) return;

            targetContext.Resources.Heal(healAmount);
            OnHealthChanged?.Invoke(targetContext.Resources.CurrentHealth, targetContext.Resources.MaxHealth);
        }

        public void SetMaxHealth(float newMaxHealth, bool keepRatio = false)
        {
            var targetContext = Context;
            if (targetContext == null) return;
            if (newMaxHealth <= 0) return;

            if (keepRatio)
            {
                float ratio = targetContext.Resources.CurrentHealth / targetContext.Resources.MaxHealth;
                maxHealth = newMaxHealth;
                targetContext.Resources.MaxHealth = newMaxHealth;
                targetContext.Resources.CurrentHealth = targetContext.Resources.MaxHealth * ratio;
            }
            else
            {
                maxHealth = newMaxHealth;
                targetContext.Resources.MaxHealth = newMaxHealth;
            }

            OnHealthChanged?.Invoke(targetContext.Resources.CurrentHealth, targetContext.Resources.MaxHealth);
        }

        public void Revive(float healthPercentage = 1f)
        {
            var targetContext = Context;
            if (targetContext == null || !targetContext.Resources.IsDead) return;

            if (!targetContext.Lifecycle.TryTransitionTo(CoCoLifecycleState.Active))
            {
                CoCoLog.Warning($"[CharacterLifeCycle] {gameObject.name} 无法从 {targetContext.Lifecycle.State} 复活到 Active。");
                return;
            }

            targetContext.Resources.Revive(healthPercentage);
            targetContext.SemanticStateId = (int)CharacterSemanticState.Alive;

            OnRevive?.Invoke();
            OnHealthChanged?.Invoke(targetContext.Resources.CurrentHealth, targetContext.Resources.MaxHealth);
        }

        public void SetContextProvider(MonoBehaviour provider)
        {
            if (ReferenceEquals(provider, this))
            {
                provider = null;
            }

            contextProvider = provider;
            _context = null;
        }

        public void ResetContextCache()
        {
            _context = null;
        }

        [Obsolete("Use ResetContextCache instead. CharacterLifeCycle no longer owns a local CharacterContext.")]
        public void ResetLocalContext()
        {
            ResetContextCache();
        }

        #endregion

        #region Internal Logic

        private void Awake()
        {
            InitializeHealth();
        }

        private void OnValidate()
        {
            if (contextProvider == this)
            {
                contextProvider = null;
            }
        }

        private void Reset()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (behaviour is ICoCoContextProvider<CharacterContext>)
                {
                    contextProvider = behaviour;
                    break;
                }
            }
        }

        private void Die(CharacterContext targetContext)
        {
            targetContext.SemanticStateId = (int)CharacterSemanticState.Dead;
            targetContext.Lifecycle.TryTransitionTo(CoCoLifecycleState.Disabled);
            OnDeath?.Invoke();
            Debug.Log($"[CharacterLifeCycle] {gameObject.name} 死亡。");
        }

        private void InitializeHealth()
        {
            var targetContext = Context;
            if (targetContext == null)
            {
                CoCoLog.Warning($"[CharacterLifeCycle] {gameObject.name} 未找到 CharacterContext。");
                return;
            }

            targetContext.Resources.MaxHealth = maxHealth;
            targetContext.Resources.Revive(1f);
            targetContext.SemanticStateId = (int)CharacterSemanticState.Alive;
            targetContext.Lifecycle.TryTransitionTo(CoCoLifecycleState.Active);
            OnHealthChanged?.Invoke(targetContext.Resources.CurrentHealth, targetContext.Resources.MaxHealth);
        }

        private CharacterContext ResolveContext()
        {
            if (_context != null) return _context;

            if (TryGetContextFromProvider(contextProvider, out _context))
            {
                return _context;
            }

            var behaviours = GetComponents<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (ReferenceEquals(behaviour, this)) continue;
                if (TryGetContextFromProvider(behaviour, out _context))
                {
                    if (contextProvider == null)
                    {
                        contextProvider = behaviour;
                    }
                    return _context;
                }
            }

            return null;
        }

        private static bool TryGetContextFromProvider(
            object provider,
            out CharacterContext targetContext)
        {
            if (provider is ICoCoContextProvider<CharacterContext> typedProvider)
            {
                targetContext = typedProvider.Context;
                return targetContext != null;
            }

            targetContext = null;
            return false;
        }

        #endregion
    }
}
