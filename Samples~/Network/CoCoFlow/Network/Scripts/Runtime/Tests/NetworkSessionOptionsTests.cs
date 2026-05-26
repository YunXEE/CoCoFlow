using Fusion;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Addon.Network.Tests
{
    [TestFixture]
    public class NetworkSessionOptionsTests
    {
        [Test]
        public void HostOptions_UseHostModeDefaults()
        {
            var options = NetSessionStartOptions.Host("RoomA", SceneRef.FromIndex(0));

            Assert.That(options.Mode, Is.EqualTo(NetSessionMode.Host));
            Assert.That(options.SessionName, Is.EqualTo("RoomA"));
            Assert.That(options.Scene.IsValid, Is.True);
            Assert.That(options.PlayerCount, Is.EqualTo(8));
            Assert.That(options.IsOpen, Is.True);
            Assert.That(options.IsVisible, Is.True);
        }

        [Test]
        public void ClientOptions_DoNotRequireScene()
        {
            var options = NetSessionStartOptions.Client("RoomB");

            Assert.That(options.Mode, Is.EqualTo(NetSessionMode.Client));
            Assert.That(options.SessionName, Is.EqualTo("RoomB"));
            Assert.That(options.Scene.IsValid, Is.False);
        }

        [Test]
        public void SharedOptions_KeepScene()
        {
            var options = NetSessionStartOptions.Shared("RoomC", SceneRef.FromIndex(1));

            Assert.That(options.Mode, Is.EqualTo(NetSessionMode.Shared));
            Assert.That(options.SessionName, Is.EqualTo("RoomC"));
            Assert.That(options.Scene.IsValid, Is.True);
        }
    }
}
