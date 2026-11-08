using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.CI
{
    public sealed class PlayerProbeTests
    {
        [Test]
        public void RunsInWindowsPlayer()
        {
            Assert.IsFalse(Application.isEditor);
            Assert.AreEqual(RuntimePlatform.WindowsPlayer, Application.platform);
            // Host preparation fills this literal from the selected profile.
            string expectedBackend = "__COCOFLOW_EXPECTED_BACKEND__";
#if ENABLE_IL2CPP
            Assert.AreEqual(expectedBackend, "IL2CPP");
#elif ENABLE_MONO
            Assert.AreEqual(expectedBackend, "Mono2x");
#else
            Assert.Fail("No supported Player scripting backend was compiled.");
#endif
        }
    }
}
