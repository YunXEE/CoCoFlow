using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.CI
{
    public sealed class PlayerProbeTests
    {
        [Test]
        public void RunsInWindowsMonoPlayer()
        {
            Assert.IsFalse(Application.isEditor);
            Assert.AreEqual(RuntimePlatform.WindowsPlayer, Application.platform);
#if !ENABLE_MONO
            Assert.Fail("This probe must execute in a Mono Player.");
#endif
        }
    }
}
