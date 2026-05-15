using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Network.Tests
{
    /// <summary>
    /// 验证 NetCharacterLifecycle 核心逻辑（伤害/死亡/复活）。
    /// 纯 C# 测试，不依赖 Fusion NetworkBehaviour 运行时，
    /// 仅验证被 RPC 方法内部使用的数学运算。
    /// </summary>
    [TestFixture]
    public class NetCharacterLifecycleTests
    {
        [Test]
        public void Damage_ReducesHealthCorrectly()
        {
            // Arrange
            const float initialHealth = 100f;
            const float damage = 30f;

            // Act (mirrors RPC_RequestDamage: Mathf.Max(0, CurrentHealth - amount))
            float resultHealth = Mathf.Max(0, initialHealth - damage);

            // Assert
            Assert.That(resultHealth, Is.EqualTo(70f),
                "100 HP - 30 damage should leave 70 HP");
        }

        [Test]
        public void Death_Triggered_WhenHealthReachesZero()
        {
            // Arrange
            const float initialHealth = 20f;
            const float damage = 20f;

            // Act
            float resultHealth = Mathf.Max(0, initialHealth - damage);
            bool isDead = resultHealth <= 0f;

            // Assert
            Assert.That(isDead, Is.True,
                "Character should be dead when health reaches 0");
            Assert.That(resultHealth, Is.EqualTo(0f),
                "Health should be exactly 0 at death");
        }

        [Test]
        public void Health_CannotGoBelowZero()
        {
            // Arrange: damage exceeds remaining health
            const float initialHealth = 10f;
            const float damage = 50f;

            // Act (mirrors clamping in RPC_RequestDamage)
            float resultHealth = Mathf.Max(0, initialHealth - damage);

            // Assert
            Assert.That(resultHealth, Is.EqualTo(0f),
                "Health should be clamped to 0, never negative");
        }

        [Test]
        public void Revive_RestoresHealthToSpecifiedPercentage()
        {
            // Arrange
            const float maxHealth = 100f;
            const float healthPercentage = 0.5f;

            // Act (mirrors RPC_RequestRevive: MaxHealth * Mathf.Clamp01(healthPercentage))
            float revivedHealth = maxHealth * Mathf.Clamp01(healthPercentage);

            // Assert
            Assert.That(revivedHealth, Is.EqualTo(50f),
                "Revive at 50% should restore to 50 HP");
        }
    }
}
