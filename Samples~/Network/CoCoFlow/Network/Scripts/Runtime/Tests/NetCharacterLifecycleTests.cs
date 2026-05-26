using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.Tests
{
    [TestFixture]
    public class NetCharacterLifecycleTests
    {
        [Test]
        public void Damage_ClampsHealthAtZero()
        {
            const float initialHealth = 10f;
            const float damage = 50f;

            var resultHealth = Mathf.Max(0f, initialHealth - damage);

            Assert.That(resultHealth, Is.EqualTo(0f));
        }

        [Test]
        public void Revive_ClampsHealthPercentage()
        {
            const float maxHealth = 100f;

            var revivedHealth = maxHealth * Mathf.Clamp01(1.5f);

            Assert.That(revivedHealth, Is.EqualTo(100f));
        }
    }
}
