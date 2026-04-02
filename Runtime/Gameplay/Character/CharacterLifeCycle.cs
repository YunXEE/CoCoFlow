using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    public class CharacterLifeCycle : MonoBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        // events
        public event Action<float, float> OnHealthChanged; 
        public event Action<float> OnTakeDamage;
        public event Action OnDeath;
        public event Action OnRevive;

        private void Awake()
        {
            InitializeHealth();
        }

        #region Public API
        
        public void TakeDamage(float damageAmount)
        {
            if (IsDead || damageAmount <= 0) return;

            CurrentHealth -= damageAmount;
            CurrentHealth = Mathf.Clamp(CurrentHealth, 0, maxHealth);
            
            OnTakeDamage?.Invoke(damageAmount);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
            
            if (CurrentHealth <= 0)
            {
                Die();
            }
        }
        
        public void Heal(float healAmount)
        {
            if (IsDead || healAmount <= 0) return;

            CurrentHealth += healAmount;
            CurrentHealth = Mathf.Clamp(CurrentHealth, 0, maxHealth);

            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }
        
        public void SetMaxHealth(float newMaxHealth, bool keepRatio = false)
        {
            if (newMaxHealth <= 0) return;

            if (keepRatio)
            {
                float ratio = CurrentHealth / maxHealth;
                maxHealth = newMaxHealth;
                CurrentHealth = maxHealth * ratio;
            }
            else
            {
                maxHealth = newMaxHealth;
                CurrentHealth = Mathf.Clamp(CurrentHealth, 0, maxHealth);
            }

            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }
        
        public void Revive(float healthPercentage = 1f)
        {
            if (!IsDead) return;
                    
            IsDead = false;
            CurrentHealth = maxHealth * Mathf.Clamp01(healthPercentage);
                    
            OnRevive?.Invoke();
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }
        
        #endregion

        #region Internal Logic

        private void Die()
        {
            IsDead = true;
            OnDeath?.Invoke();
            Debug.Log($"[CharacterLifeCycle] {gameObject.name} 死亡。");
        }
        
        private void InitializeHealth()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        #endregion

    }
}