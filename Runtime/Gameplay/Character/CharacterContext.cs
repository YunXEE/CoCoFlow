using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    public enum CharacterSemanticState
    {
        Unknown = 0,
        Alive = 1,
        Dead = 2
    }

    [Serializable]
    public class CharacterIntent : ICoCoIntent
    {
        public Vector2 move;
        public Vector2 look;
        public Vector3 desiredMovePosition;
        public bool hasMovePosition;
        public Transform desiredTarget;
        public string desiredTargetId;
        public bool jump;
        public bool attack;
        public bool interact;
        public bool useSkill;

        public void CopyFrom(CharacterIntent source)
        {
            if (source == null)
            {
                Clear();
                return;
            }

            move = source.move;
            look = source.look;
            desiredMovePosition = source.desiredMovePosition;
            hasMovePosition = source.hasMovePosition;
            desiredTarget = source.desiredTarget;
            desiredTargetId = source.desiredTargetId;
            jump = source.jump;
            attack = source.attack;
            interact = source.interact;
            useSkill = source.useSkill;
        }

        public void Clear()
        {
            move = Vector2.zero;
            look = Vector2.zero;
            desiredMovePosition = Vector3.zero;
            hasMovePosition = false;
            desiredTarget = null;
            desiredTargetId = string.Empty;
            ClearDiscrete();
        }

        public void ClearDiscrete()
        {
            jump = false;
            attack = false;
            interact = false;
            useSkill = false;
        }
    }

    [Serializable]
    public class CharacterMotionContext
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 velocity;
        public bool isGrounded;
    }

    [Serializable]
    public class CharacterResourceContext
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;

        public float MaxHealth
        {
            get => maxHealth;
            set
            {
                maxHealth = Mathf.Max(1f, value);
                currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            }
        }

        public float CurrentHealth
        {
            get => currentHealth;
            set => currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        }

        public bool IsDead => currentHealth <= 0f;

        public bool ApplyDamage(float amount)
        {
            if (amount <= 0f || IsDead) return IsDead;
            CurrentHealth = currentHealth - amount;
            return IsDead;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead) return;
            CurrentHealth = currentHealth + amount;
        }

        public void Revive(float healthPercentage = 1f)
        {
            CurrentHealth = maxHealth * Mathf.Clamp01(healthPercentage);
        }
    }

    [Serializable]
    public class CharacterPerceptionContext
    {
        public Transform currentTarget;
        public string currentTargetId;
        public Vector3 lastKnownPosition;
        public bool isTargetVisible;
    }

    [Serializable]
    public class CharacterNavigationContext
    {
        public Vector3 destination;
        public Vector3 desiredVelocity;
        public bool hasDestination;
    }

    [Serializable]
    public class CharacterContext : CoCoEntityContext
    {
        [SerializeField] private CharacterIntent intent = new CharacterIntent();
        [SerializeField] private CharacterMotionContext motion = new CharacterMotionContext();
        [SerializeField] private CharacterResourceContext resources = new CharacterResourceContext();
        [SerializeField] private CharacterPerceptionContext perception = new CharacterPerceptionContext();
        [SerializeField] private CharacterNavigationContext navigation = new CharacterNavigationContext();

        public CharacterIntent Intent => intent;
        public CharacterMotionContext Motion => motion;
        public CharacterResourceContext Resources => resources;
        public CharacterPerceptionContext Perception => perception;
        public CharacterNavigationContext Navigation => navigation;

        public void MarkAlive()
        {
            SemanticStateId = (int)CharacterSemanticState.Alive;
            Lifecycle.TransitionTo(CoCoLifecycleState.Active);
        }

        public void MarkDeadDisabled()
        {
            SemanticStateId = (int)CharacterSemanticState.Dead;
            Lifecycle.TransitionTo(CoCoLifecycleState.Disabled);
        }
    }
}
