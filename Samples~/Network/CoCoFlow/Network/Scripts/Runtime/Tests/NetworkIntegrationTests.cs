using Fusion;
using NUnit.Framework;
using System.Linq;

namespace CoCoFlow.Runtime.Addon.Network.Tests
{
    [TestFixture]
    public class NetworkIntegrationTests : NetworkTestSetup
    {
        [Test]
        public void NetManager_RegistersRunnerAndSessionInterfaces()
        {
            var interfaces = typeof(NetManager).GetInterfaces();

            Assert.That(interfaces.Contains(typeof(INetworkRunnerProvider)), Is.True);
            Assert.That(interfaces.Contains(typeof(INetworkSessionController)), Is.True);
        }

        [Test]
        public void NetworkPlayerSpawner_CanBeCreatedWithoutPrefab()
        {
            var obj = CreateTestObject("Spawner");
            var spawner = obj.AddComponent<NetworkPlayerSpawner>();

            Assert.That(spawner, Is.Not.Null);
        }

        [Test]
        public void SceneRef_Default_IsInvalidForClientJoin()
        {
            var options = NetSessionStartOptions.Client("JoinOnly");

            Assert.That(options.Scene, Is.EqualTo(default(SceneRef)));
            Assert.That(options.Scene.IsValid, Is.False);
        }
    }
}
